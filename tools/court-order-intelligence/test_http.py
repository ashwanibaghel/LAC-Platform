import contextlib
import http.client
import io
import json
import tempfile
import threading
import unittest
from http.server import HTTPServer
from pathlib import Path
from unittest.mock import patch
import serve_questions
from test_worker import fact,order

CASE='a1000000-0000-4000-8000-000000000001'

class RuntimeTests(unittest.TestCase):
    def test_ai_off_leaves_stored_intelligence_readable(self):
        class Offline:
            calls=0
            def extract(self,*args):
                self.calls+=1
                raise ConnectionError('Local model is off')
        provider=Offline()
        with tempfile.TemporaryDirectory() as root:
            artifact=Path(root)/'court-intelligence'/'v1'/CASE/'current.json'
            artifact.parent.mkdir(parents=True)
            artifact.write_text(json.dumps({'version':1,'caseId':CASE,'caseNumber':'W.P.(C) 1/2026','orders':[order([fact()])]}))
            captured=[]
            class Capture:
                def __init__(self,address,handler): captured.append(handler)
                def serve_forever(self): pass
            with patch('sys.argv',['questions','--extraction-root',root,'--model-version','test','--demo']),patch.object(serve_questions,'HTTPServer',Capture),patch.object(serve_questions,'LlamaCppProvider',return_value=provider),contextlib.redirect_stdout(io.StringIO()):
                serve_questions.main()
            self.assertEqual(0,provider.calls) # startup/render never makes inference
            server=HTTPServer(('127.0.0.1',0),captured[0])
            thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
            connection=http.client.HTTPConnection('127.0.0.1',server.server_port,timeout=3)
            try:
                route=f'/api/court-cases/{CASE}/intelligence'
                connection.request('GET',route)
                response=connection.getresponse();self.assertEqual(200,response.status);response.read()
                self.assertEqual(0,provider.calls)
                connection.request('POST',route+'/ask',json.dumps({'question':'What did latest order direct?'}),{'Content-Type':'application/json'})
                response=connection.getresponse();self.assertEqual(503,response.status)
                self.assertIn('Case intelligence remains available',response.read().decode())
                connection.request('GET',route)
                response=connection.getresponse();self.assertEqual(200,response.status);response.read()
                self.assertEqual(1,provider.calls)
            finally:
                connection.close();server.shutdown();server.server_close();thread.join(2)

if __name__=='__main__':unittest.main()
