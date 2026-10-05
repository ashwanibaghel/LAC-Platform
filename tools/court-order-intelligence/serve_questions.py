"""Loopback Q&A; exact known date may lazily fetch one official temporary PDF."""
import argparse
import json
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer as HTTPServer
from pathlib import Path
from provider import LlamaCppProvider
from questions import answer, INSUFFICIENT
from order_index import merge_known_orders,prepare_question,MAX_CASE_ARTIFACT_BYTES,worker_busy
from real_case import RefreshController, read_artifact, reusable_record
from chat_router import route, general_answer
from question_language import selected_language, localize
from runtime_health import model_state, root_fingerprint, case_state
from runtime_recovery import package_digest

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
    parser.add_argument('--require-model-health',action='store_true')
    args=parser.parse_args()
    root=Path(args.extraction_root)
    package_sha=package_digest(Path(__file__).parent)
    if not root.is_absolute() or not root.is_dir(): raise SystemExit('Existing absolute extraction root required')
    provider=LlamaCppProvider(args.endpoint,args.model_version,request_timeout=240)
    refresh=RefreshController(root,lambda:LlamaCppProvider(args.endpoint,args.model_version,
                               request_timeout=args.refresh_request_timeout_seconds),
                               timeout_seconds=args.refresh_case_timeout_seconds)
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass # no questions/evidence in access logs
        def do_GET(self):
            if self.path=='/health':
                body=json.dumps(dict(questionServiceState='Ready',modelState=model_state(),
                    modelVersion=args.model_version,extractionRootFingerprint=root_fingerprint(root),
                    refreshActive=refresh.lock.locked(),questionPackageSha256=package_sha)).encode()
                try:
                    self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Cache-Control','no-store')
                    self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
                except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError): pass
                return
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
            if self.path not in ('/ask','/refresh','/status') and not demo_route: self.send_error(404); return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 0<length<=512*1024: raise ValueError('Request size') # bounded trusted known-order metadata
                request=json.loads(self.rfile.read(length))
                case_id=demo_route[1] if demo_route else str(uuid.UUID(request['caseId']))
                if self.path=='/status':
                    artifact=read_artifact(root,case_id,request.get('caseNumber'))
                    if request.get('caseNumber') and isinstance(request.get('orderIndex'),list):
                        artifact=merge_known_orders(artifact,case_id,request['caseNumber'],request['orderIndex'][:1000],strict_index=True)
                    ms=model_state();state=refresh.snapshot(case_id)
                    cs=case_state(artifact)
                    state.update(modelState=ms,questionServiceState='Ready',caseState=cs,
                        runtimeState=ms if ms!='Ready' else 'Processing' if refresh.active_case_id==case_id else 'BusyWithOtherCase' if refresh.active_case_id or worker_busy(root/'court-intelligence'/'v1') else 'Failed' if state.get('status') in ('Failed','Interrupted') else cs,
                        extractionRootFingerprint=root_fingerprint(root),modelVersion=args.model_version,questionPackageSha256=package_sha)
                    self.write_json(state,200);return
                if self.path=='/refresh':
                    if args.require_model_health and (ms:=model_state())!='Ready':
                        from semantics import VERSION
                        saved=merge_known_orders(read_artifact(root,case_id,request['caseNumber']),case_id,request['caseNumber'],request['orderIndex'],strict_index=True)
                        versions={'extraction':VERSION,'rulebook':'2','model':args.model_version}
                        if not saved['orders'] or not all(reusable_record(o,versions)for o in saved['orders']):
                            self.write_json(dict(caseId=case_id,runtimeState=ms,reasonCode=ms,
                                error='Verified local model is not ready; use runtime recovery.'),503);return
                    result=refresh.start(case_id,request['caseNumber'],request['orderIndex'])
                    self.write_json(result,409 if result.get('runtimeState')=='BusyWithOtherCase' else 202)
                    return
                question=request['question']
                if not isinstance(question,str) or not 1<=len(question.strip())<=600: raise ValueError('Question length')
                language=selected_language(request.get('language','Auto'),question)
                if route(question)=='GeneralLocal':
                    result=general_answer(question,provider,request.get('appContext'),request.get('history'),language=language)
                else:
                    artifact=read_artifact(root,case_id,request.get('caseNumber'))
                    if not demo_route and request.get('caseNumber') and isinstance(request.get('orderIndex'),list):
                        artifact=merge_known_orders(artifact,case_id,request['caseNumber'],request['orderIndex'][:1000],strict_index=True)
                    if artifact is None:
                        result=localize({'answer':INSUFFICIENT,'claims':[],'insufficientEvidence':True},language)
                    else:
                        # Reads never acquire the PDF writer lock. Lazy retrieval
                        # of one exact date remains optional while a refresh runs.
                        inference_ready=not args.require_model_health or model_state()=='Ready'
                        if inference_ready and not refresh.lock.locked():
                            artifact=prepare_question(root,artifact,case_id,question,provider,strict_index=not bool(demo_route))
                        result=answer(artifact,case_id,question,provider,request.get('courtCoverage'),
                             background_processing=refresh.active_case_id==case_id,inference_busy=refresh.lock.locked(),
                             language=language,conversation_context=request.get('conversationContext'),
                              model_available=inference_ready)
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
        def write_json(self,result,status):
            body=json.dumps(result,ensure_ascii=False).encode()
            self.send_response(status);self.send_header('Content-Type','application/json');self.send_header('Cache-Control','no-store')
            self.send_header('Content-Length',str(len(body)));self.end_headers();self.wfile.write(body)
    print(f'Court case Q&A listening on 127.0.0.1:{args.port}',flush=True)
    HTTPServer(('127.0.0.1',args.port),Handler).serve_forever()

if __name__=='__main__': main()
