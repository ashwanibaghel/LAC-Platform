import json
import tempfile
import threading
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch
from real_case import refresh_case, read_artifact, RefreshController
from order_index import merge_known_orders
from semantics import synthesize
from worker import atomic_json
from test_worker import order, fact

CASE=str(uuid.uuid4()); OTHER=str(uuid.uuid4()); NUMBER='W.P.(C) 42/2026'
URL='https://delhihighcourt.nic.in/app/showlogo/fixture.pdf/2026'


def source(case=CASE,day='2026-01-01',url=URL):
    return dict(courtCaseId=case,normalizedCaseIdentity='delhihighcourt|wpc|42|2026',
                orderDate=day,officialUrl=url,sourceObservationId=str(uuid.uuid4()),
                corrigendumUrl=None,uploadDate='2026-01-02')


def write_existing(root,records):
    artifact=synthesize(CASE,NUMBER,records); artifact['processingComplete']=True
    atomic_json(Path(root)/'court-intelligence'/'v1'/CASE/'current.json',artifact)
    return artifact


class RealCaseTests(unittest.TestCase):
    def test_registered_id_and_actual_metadata_drive_processor_and_artifact(self):
        index=[source(),source(day='2026-02-01',url=URL.replace('fixture','second'))]; seen=[]
        def process(metadata,number,provider):
            seen.append((metadata.copy(),number))
            return order([fact()],officialUrl=metadata['officialUrl'],orderDate=metadata['orderDate'])
        with tempfile.TemporaryDirectory() as root:
            result,reviews=refresh_case(root,CASE,NUMBER,index,object(),processor=process)
            self.assertEqual(CASE,result['caseId']); self.assertEqual(2,len(seen))
            self.assertEqual(NUMBER,seen[0][1]); self.assertEqual(index[1]['sourceObservationId'],seen[0][0]['sourceObservationId'])
            self.assertEqual(['2026-02-01','2026-01-01'],[entry[0]['orderDate'] for entry in seen])
            self.assertEqual(index[0]['sourceObservationId'],result['orderIndex'][0]['sourceObservationId'])
            self.assertEqual(index[0]['normalizedCaseIdentity'],result['orderIndex'][0]['normalizedCaseIdentity'])
            self.assertEqual(CASE,result['orders'][0]['courtCaseId'])
            self.assertEqual(0,reviews); self.assertTrue(result['processingComplete'])
            self.assertEqual(result,read_artifact(root,CASE,NUMBER))
            self.assertIsNone(read_artifact(root,OTHER)); self.assertEqual([],list(Path(root).rglob('*.pdf')))

    def test_partial_failure_shows_only_verified_material_and_coverage_gap(self):
        index=[source(),source(day='2026-02-01',url=URL.replace('fixture','second'))]
        def process(src,*args):
            return order([fact()] if src['orderDate']=='2026-01-01' else [],
                status='Validated' if src['orderDate']=='2026-01-01' else 'NeedsSourceReview',
                officialUrl=src['officialUrl'],orderDate=src['orderDate'],
                **({} if src['orderDate']=='2026-01-01' else {'failureMessage':'Unusable native text'}))
        with tempfile.TemporaryDirectory() as root:
            result,reviews=refresh_case(root,CASE,NUMBER,index,object(),processor=process)
            self.assertEqual(1,reviews); self.assertEqual(1,result['sourceCoverage']['checkedSources'])
            self.assertEqual([],result['orders'][1]['facts']); self.assertEqual([],result['currentPosition'])
            self.assertEqual(1,len(result['beforeNextHearing'])); self.assertEqual(1,len(result['sourceCoverage']['gaps']))

    def test_model_unavailable_preserves_previous_verified_evidence_and_discloses_failure(self):
        with tempfile.TemporaryDirectory() as root:
            previous=write_existing(root,[order([fact()],officialUrl=URL)])
            result,reviews=refresh_case(root,CASE,NUMBER,[source()],object(),processor=lambda src,*args:
                order([],status='NeedsReview',officialUrl=URL,failureMessage='Local model unavailable'))
            self.assertEqual(previous['beforeNextHearing'],result['beforeNextHearing'])
            self.assertEqual(1,reviews); self.assertIn('previously verified',result['sourceCoverage']['gaps'][0]['reason'])

    def test_unexpected_processor_failure_does_not_replace_current_snapshot(self):
        with tempfile.TemporaryDirectory() as root:
            previous=write_existing(root,[order([fact()],officialUrl=URL)])
            def bad(*args): raise OSError('fixture storage failure')
            with self.assertRaises(OSError): refresh_case(root,CASE,NUMBER,[source()],object(),processor=bad)
            self.assertEqual(previous,read_artifact(root,CASE))

    def test_unknown_cross_case_sources_are_not_retained_or_fetched(self):
        with tempfile.TemporaryDirectory() as root:
            existing=write_existing(root,[order([fact()],officialUrl=URL)])
            scoped=merge_known_orders(existing,CASE,NUMBER,[source(case=OTHER)],strict_index=True)
            self.assertEqual([],scoped['orders'])
            with self.assertRaises(ValueError): refresh_case(root,CASE,NUMBER,[source(case=OTHER)],object(),
                processor=lambda *args:self.fail('Cross-case PDF fetch'))

    def test_oversized_snapshot_never_replaces_previous_readable_artifact(self):
        with tempfile.TemporaryDirectory() as root:
            previous=write_existing(root,[order([fact()],officialUrl=URL)])
            with self.assertRaises(ValueError):
                refresh_case(root,CASE,NUMBER,[source()],object(),processor=lambda *args:
                    order([fact('x'*(2*1024*1024))],officialUrl=URL))
            self.assertEqual(previous,read_artifact(root,CASE,NUMBER))

    def test_wrong_embedded_case_or_malformed_artifact_fails_closed(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'court-intelligence'/'v1'/CASE/'current.json'
            for value in [{'version':1,'caseId':OTHER,'orders':[]},{'version':1,'caseId':CASE,'orders':'bad'}]:
                atomic_json(path,value)
                with self.assertRaises(ValueError): read_artifact(root,CASE)
            path.write_text('{broken')
            with self.assertRaises(ValueError): read_artifact(root,CASE)

    def test_restart_marks_refresh_interrupted_without_model_or_download(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'court-intelligence'/'v1'/CASE/'refresh.json'
            atomic_json(path,dict(caseId=CASE,status='Running',checked=1,total=2))
            RefreshController(root,lambda:self.fail('Startup inference'))
            self.assertEqual('Interrupted',json.loads(path.read_text())['status'])

    def test_refresh_budget_is_configurable_bounded_and_restores_provider_timeout(self):
        class Provider:
            version='fixture'
            request_timeout=300
            def extract(self,*args): return self.request_timeout
        provider=Provider(); seen=[]
        def process(src,number,bounded):
            seen.append(bounded.extract());return order([fact()],officialUrl=URL)
        with tempfile.TemporaryDirectory() as root:
            refresh_case(root,CASE,NUMBER,[source()],provider,processor=process,timeout_seconds=1800)
        self.assertEqual([300],seen); self.assertEqual(300,provider.request_timeout)
        for invalid in [0,1801,float('inf'),None]:
            with self.assertRaises(ValueError): RefreshController('.',lambda:provider,timeout_seconds=invalid)

    def test_whole_budget_caps_each_request_and_expiration_is_fail_closed(self):
        class Provider:
            version='fixture'
            request_timeout=300
            def extract(self,*args): return self.request_timeout
        provider=Provider(); seen=[]
        def process(src,number,bounded):
            seen.append(bounded.extract());return order([fact()],officialUrl=URL)
        with tempfile.TemporaryDirectory() as root:
            refresh_case(root,CASE,NUMBER,[source()],provider,processor=process,timeout_seconds=10)
        self.assertLessEqual(seen[0],10);self.assertEqual(300,provider.request_timeout)
        with tempfile.TemporaryDirectory() as root, patch('real_case.time.monotonic',side_effect=[0,11]):
            with self.assertRaises(ValueError):refresh_case(root,CASE,NUMBER,[source()],provider,processor=process,timeout_seconds=10)
            self.assertIsNone(read_artifact(root,CASE))

    def test_only_one_case_refresh_can_start_and_explicit_retry_is_possible(self):
        entered=threading.Event(); finish=threading.Event()
        def blocked(*args,**kwargs): entered.set(); finish.wait(3); return {},0
        with tempfile.TemporaryDirectory() as root, patch('real_case.refresh_case',side_effect=blocked):
            controller=RefreshController(root,lambda:object())
            self.assertEqual(CASE,controller.start(CASE,NUMBER,[source()])['caseId'])
            self.assertTrue(entered.wait(2))
            same=controller.start(CASE,NUMBER,[source()])
            self.assertEqual('Processing',same['runtimeState']);self.assertIn('startedAt',same)
            self.assertEqual('BusyWithOtherCase',controller.start(OTHER,NUMBER,[source(case=OTHER)])['runtimeState'])
            finish.set()
            self.assertTrue(controller.lock.acquire(timeout=3)); controller.lock.release()
            state=json.loads((Path(root)/'court-intelligence'/'v1'/CASE/'refresh.json').read_text())
            self.assertEqual('Completed',state['status'])


if __name__=='__main__': unittest.main()
