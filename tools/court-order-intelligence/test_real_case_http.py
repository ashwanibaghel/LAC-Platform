"""Real loopback HTTP contract with synthetic evidence; never calls DHC/model."""
import json
import tempfile
import threading
import unittest
import uuid
from http.server import HTTPServer
from pathlib import Path
from unittest.mock import patch
import requests
import serve_questions
import real_case
from real_case import refresh_case, read_artifact
from test_real_case import source, NUMBER, URL
from test_worker import order, fact
from test_questions import SelectAll


class RegisteredHttpTests(unittest.TestCase):
    def test_registered_refresh_and_grounded_question_use_same_actual_case_index(self):
        case=str(uuid.uuid4()); other=str(uuid.uuid4()); ready=threading.Event(); completed=threading.Event()
        servers=[]; seen=[]
        def processor(metadata,number,provider):
            seen.append((metadata.copy(),number))
            return order([fact()],officialUrl=metadata['officialUrl'],orderDate=metadata['orderDate'])
        def refresh(*args,**kwargs):
            try: return refresh_case(*args,processor=processor,**kwargs)
            finally: completed.set()
        def server(_address,handler):
            actual=HTTPServer(('127.0.0.1',0),handler); servers.append(actual); ready.set(); return actual
        with tempfile.TemporaryDirectory() as root, patch.object(serve_questions,'HTTPServer',server), \
                patch.object(serve_questions,'LlamaCppProvider',lambda *args,**kwargs:SelectAll()), \
                patch.object(real_case,'refresh_case',refresh), \
                patch('sys.argv',['serve_questions','--extraction-root',root,'--model-version','synthetic-local-fixture']):
            thread=threading.Thread(target=serve_questions.main,daemon=True); thread.start()
            self.assertTrue(ready.wait(3)); base='http://127.0.0.1:'+str(servers[0].server_port)
            session=requests.Session(); session.trust_env=False
            try:
                self.assertEqual(404,session.get(base+'/api/court-cases/a1000000-0000-4000-8000-000000000001/intelligence',timeout=3).status_code)
                response=session.post(base+'/refresh',json={'caseId':case,'caseNumber':NUMBER,'orderIndex':[source(case=case)]},timeout=3)
                self.assertEqual(202,response.status_code); self.assertEqual(case,response.json()['caseId'])
                self.assertEqual('no-store',response.headers['Cache-Control']); self.assertTrue(completed.wait(3))
                artifact=read_artifact(root,case,NUMBER)
                self.assertEqual(case,artifact['caseId']); self.assertEqual(NUMBER,seen[0][1])
                self.assertEqual(case,seen[0][0]['courtCaseId']); self.assertEqual(URL,seen[0][0]['officialUrl'])
                self.assertIsNone(read_artifact(root,other)); self.assertEqual([],list(Path(root).rglob('*.pdf')))
                answer=session.post(base+'/ask',json={'caseId':case,'caseNumber':NUMBER,'question':'What did the latest order direct?',
                    'orderIndex':[source(case=case)]},timeout=3)
                self.assertEqual(200,answer.status_code); self.assertEqual(case,answer.json()['caseId'])
                self.assertTrue(answer.json()['claims']); self.assertEqual(URL,answer.json()['claims'][0]['source']['officialUrl'])
                self.assertEqual(1,answer.json()['claims'][0]['source']['page'])
                self.assertEqual(1,len(seen)) # Q&A did not re-download verified evidence.
                unsupported=session.post(base+'/ask',json={'caseId':other,'caseNumber':NUMBER,'question':'What did the Court direct?',
                    'orderIndex':[source(case=case)]},timeout=3)
                self.assertEqual(200,unsupported.status_code); self.assertEqual([],unsupported.json()['claims'])
                self.assertEqual(1,len(seen))
                rejected=session.post(base+'/refresh',json={'caseId':other,'caseNumber':NUMBER,'orderIndex':[source(case=case)]},timeout=3)
                self.assertEqual(503,rejected.status_code); self.assertEqual(1,len(seen))
            finally:
                session.close(); servers[0].shutdown(); servers[0].server_close(); thread.join(3)


if __name__=='__main__': unittest.main()
