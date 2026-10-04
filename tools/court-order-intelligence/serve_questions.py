"""Loopback Q&A; exact known date may lazily fetch one official temporary PDF."""
import argparse
import json
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer as HTTPServer
from pathlib import Path
from provider import LlamaCppProvider
from questions import answer, INSUFFICIENT
from order_index import merge_known_orders,prepare_question,MAX_CASE_ARTIFACT_BYTES
from real_case import RefreshController, read_artifact
from chat_router import route, general_answer

def registered_answer(root,case_id,request,provider,refresh,demo=False):
    question=request['question']
    artifact=read_artifact(root,case_id,request.get('caseNumber'))
    if not demo and request.get('caseNumber') and isinstance(request.get('orderIndex'),list):
        artifact=merge_known_orders(artifact,case_id,request['caseNumber'],request['orderIndex'][:1000],strict_index=True)
    if artifact is None: return {'answer':INSUFFICIENT,'claims':[],'insufficientEvidence':True}
    if not request.get('structuredOnly') and not refresh.lock.locked():
        artifact=prepare_question(root,artifact,case_id,question,provider,strict_index=not demo)
    return answer(artifact,case_id,question,provider,request.get('courtCoverage'),
        background_processing=refresh.active_case_id==case_id,inference_busy=refresh.lock.locked() or bool(request.get('deterministicOnly')),
        conversation_questions=request.get('conversationQuestions') if request.get('groundedOnly') else None)

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--extraction-root',required=True)
    parser.add_argument('--model-version',required=True)
    parser.add_argument('--endpoint',default='http://127.0.0.1:8096')
    parser.add_argument('--port',type=int,default=8097)
    parser.add_argument('--refresh-request-timeout-seconds',type=int,choices=range(1,1801),default=600,
                        metavar='1..1800',help='Bounded refresh inference timeout, capped by the per-order budget; Q&A unchanged')
    parser.add_argument('--refresh-case-timeout-seconds',type=int,choices=range(1,1801),default=900,
                        metavar='1..1800',help='Bounded per-order processing budget; complete history is incremental')
    parser.add_argument('--demo',action='store_true',help='Explicit isolated public-order demo; never enables registered-case search')
    args=parser.parse_args()
    root=Path(args.extraction_root)
    if not root.is_absolute() or not root.is_dir(): raise SystemExit('Existing absolute extraction root required')
    provider=LlamaCppProvider(args.endpoint,args.model_version,request_timeout=240)
    refresh=RefreshController(root,lambda:LlamaCppProvider(args.endpoint,args.model_version,
                               request_timeout=args.refresh_request_timeout_seconds),
                               timeout_seconds=args.refresh_case_timeout_seconds)
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass # no questions/evidence in access logs
        def do_GET(self):
            if not args.demo:
                self.send_error(404); return
            import re
            match=re.fullmatch(r'/api/court-cases/(a1000000-0000-4000-8000-0000000000(?:0[1-9]|1[0123]))/intelligence',self.path)
            if not match:
                self.send_error(404); return
            path=root/'court-intelligence'/'v1'/match[1]/'current.json'
            if not path.is_file(): self.send_response(204); self.end_headers(); return
            body=path.read_bytes()
            if len(body)>MAX_CASE_ARTIFACT_BYTES: self.send_error(503); return
            artifact=json.loads(body)
            if artifact.get('caseId')!=match[1]: self.send_error(503); return
            self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
            self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
        def do_POST(self):
            import re
            demo_route=re.fullmatch(r'/api/court-cases/(a1000000-0000-4000-8000-0000000000(?:0[1-9]|1[0123]))/intelligence/ask',self.path) if args.demo else None
            if self.path not in ('/ask','/refresh','/assistant/general') and not demo_route: self.send_error(404); return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 0<length<=512*1024: raise ValueError('Request size') # bounded trusted known-order metadata
                request=json.loads(self.rfile.read(length))
                if self.path=='/assistant/general':
                    question=request['question']
                    if not isinstance(question,str) or not 1<=len(question.strip())<=600: raise ValueError('Question length')
                    result=general_answer(question,provider,request.get('appContext'),request.get('history'),standalone=True)
                    body=json.dumps(result,ensure_ascii=False).encode()
                    self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
                    self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
                    return
                case_id=demo_route[1] if demo_route else str(uuid.UUID(request['caseId']))
                if self.path=='/refresh':
                    result=refresh.start(case_id,request['caseNumber'],request['orderIndex'])
                    body=json.dumps(result or {'error':'Another local Court intelligence check is in progress.'}).encode()
                    self.send_response(202 if result else 409)
                    self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
                    self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
                    return
                question=request['question']
                if not isinstance(question,str) or not 1<=len(question.strip())<=600: raise ValueError('Question length')
                if not request.get('groundedOnly') and route(question)=='GeneralLocal':
                    result=general_answer(question,provider,request.get('appContext'),request.get('history'))
                else:
                    # Global conversations forbid implicit source processing;
                    # accepted legacy explicit-retry behavior remains optional.
                    result=registered_answer(root,case_id,request,provider,refresh,bool(demo_route))
                    result['mode']='CourtGrounded'
                result['caseId']=case_id
                body=json.dumps(result,ensure_ascii=False).encode()
                self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
                self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                return # Navigation may cancel an answer; never retry/replay it.
            except Exception:
                body=json.dumps({'error':'Question answering is temporarily unavailable. Case intelligence remains available.'}).encode()
                self.send_response(503); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
                try:
                    self.end_headers(); self.wfile.write(body)
                except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError): pass
    print(f'Court case Q&A listening on 127.0.0.1:{args.port}',flush=True)
    HTTPServer(('127.0.0.1',args.port),Handler).serve_forever()

if __name__=='__main__': main()
