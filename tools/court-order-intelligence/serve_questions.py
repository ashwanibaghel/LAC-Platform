"""Out-of-process Q&A runtime. Loopback-only, sequential, no outbound PDF traffic."""
import argparse
import json
import uuid
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from provider import LlamaCppProvider
from questions import answer, INSUFFICIENT

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--extraction-root',required=True)
    parser.add_argument('--model-version',required=True)
    parser.add_argument('--endpoint',default='http://127.0.0.1:8096')
    parser.add_argument('--port',type=int,default=8097)
    parser.add_argument('--demo',action='store_true',help='Explicit isolated public-order demo; never enables registered-case search')
    args=parser.parse_args()
    root=Path(args.extraction_root)
    if not root.is_absolute() or not root.is_dir(): raise SystemExit('Existing absolute extraction root required')
    provider=LlamaCppProvider(args.endpoint,args.model_version,request_timeout=240)
    class Handler(BaseHTTPRequestHandler):
        def log_message(self,*args): pass # no questions/evidence in access logs
        def do_GET(self):
            if not args.demo:
                self.send_error(404); return
            import re
            match=re.fullmatch(r'/api/court-cases/(a1000000-0000-4000-8000-0000000000(?:0[1-9]|1[012]))/intelligence',self.path)
            if not match:
                self.send_error(404); return
            path=root/'court-intelligence'/'v1'/match[1]/'current.json'
            if not path.is_file(): self.send_response(204); self.end_headers(); return
            body=path.read_bytes()
            if len(body)>2*1024*1024: self.send_error(503); return
            artifact=json.loads(body)
            if artifact.get('caseId')!=match[1]: self.send_error(503); return
            self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
            self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
        def do_POST(self):
            import re
            demo_route=re.fullmatch(r'/api/court-cases/(a1000000-0000-4000-8000-0000000000(?:0[1-9]|1[012]))/intelligence/ask',self.path) if args.demo else None
            if self.path != '/ask' and not demo_route: self.send_error(404); return
            try:
                length=int(self.headers.get('Content-Length','0'))
                if not 0<length<=8192: raise ValueError('Request size')
                request=json.loads(self.rfile.read(length))
                case_id=demo_route[1] if demo_route else str(uuid.UUID(request['caseId']))
                question=request['question']
                if not isinstance(question,str) or not 1<=len(question.strip())<=600: raise ValueError('Question length')
                path=root/'court-intelligence'/'v1'/case_id/'current.json'
                if not path.is_file() or path.stat().st_size>2*1024*1024:
                    result={'answer':INSUFFICIENT,'claims':[],'insufficientEvidence':True}
                else:
                    artifact=json.loads(path.read_text(encoding='utf-8'))
                    result=answer(artifact,case_id,question,provider)
                body=json.dumps(result,ensure_ascii=False).encode()
                self.send_response(200); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
                self.send_header('Content-Length',str(len(body))); self.end_headers(); self.wfile.write(body)
            except Exception:
                body=json.dumps({'error':'Question answering is temporarily unavailable. Case intelligence remains available.'}).encode()
                self.send_response(503); self.send_header('Content-Type','application/json'); self.send_header('Cache-Control','no-store')
                self.end_headers(); self.wfile.write(body)
    print(f'Court case Q&A listening on 127.0.0.1:{args.port}',flush=True)
    HTTPServer(('127.0.0.1',args.port),Handler).serve_forever()

if __name__=='__main__': main()
