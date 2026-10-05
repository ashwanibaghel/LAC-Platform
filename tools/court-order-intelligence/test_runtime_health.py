import unittest,tempfile,json,threading,hashlib
from pathlib import Path
from unittest.mock import patch
from real_case import RefreshController
from runtime_health import case_state,model_state
from runtime_recovery import verify
from test_real_case import CASE,NUMBER,source
from test_worker import order,fact
from source_version_review import review_cached_selections

class RuntimeTests(unittest.TestCase):
    def test_missing_evidence_is_not_verified_absence_of_action(self):
        self.assertEqual('CaseNotReady',case_state(None))
        self.assertEqual('SourceBlocked',case_state({'orders':[order(status='NeedsSourceReview')]}))
        self.assertEqual('Ready',case_state({'orders':[order([fact()])]}))

    def test_offline_probe_does_not_start_process_or_inference(self):
        import requests
        with patch('requests.Session.get',side_effect=requests.ConnectionError),patch('subprocess.Popen')as launch:
            self.assertEqual('ModelOffline',model_state());launch.assert_not_called()

    def test_interrupted_sidecar_is_disclosed_after_restart(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'court-intelligence/v1'/CASE/'refresh.json';path.parent.mkdir(parents=True)
            path.write_text(json.dumps(dict(caseId=CASE,status='Running',checked=0,total=1)))
            c=RefreshController(root,lambda:object())
            self.assertEqual('Failed',c.snapshot(CASE)['runtimeState'])
            self.assertEqual('Interrupted',c.snapshot(CASE)['reasonCode'])

    def test_failed_worker_has_durable_safe_reason_and_elapsed_time(self):
        with tempfile.TemporaryDirectory()as root,patch('real_case.refresh_case',side_effect=ValueError('secret raw URL')):
            c=RefreshController(root,lambda:object());c.start(CASE,NUMBER,[source()])
            self.assertTrue(c.lock.acquire(timeout=3));c.lock.release()
            state=c.snapshot(CASE);self.assertEqual('Failed',state['runtimeState'])
            self.assertEqual('RefreshFailed',state['reasonCode']);self.assertIn('elapsedSeconds',state)
            self.assertNotIn('secret',json.dumps(state))

    def test_manifest_tamper_fails_before_any_launch(self):
        with tempfile.TemporaryDirectory()as root:
            path=Path(root)/'manifest.json';path.write_text('{}')
            with self.assertRaisesRegex(ValueError,'ManifestHashMismatch'):verify(path,'0'*64,'0'*64,root)

    def test_source_review_cannot_use_incomplete_or_other_case_prior_evidence(self):
        with tempfile.TemporaryDirectory()as root:
            for prior in [{},{'status':'NeedsSourceReview'},{'versions':{'extraction':'old'}}]:
                with self.assertRaisesRegex(ValueError,'provenance'):review_cached_selections(source(),NUMBER,prior,Path(root)/'missing.pdf','0'*64,'test')

    def test_cached_grounded_answer_survives_model_offline_without_inventing_missing_evidence(self):
        from questions import answer
        from semantics import synthesize
        class Never:
            def extract(self,*args):raise AssertionError('Offline model invoked')
        empty=synthesize(CASE,NUMBER,[order(status='NeedsSourceReview')])
        result=answer(empty,CASE,'Case simple language me samjhao',Never(),model_available=False)
        self.assertEqual('UnavailableUntilVerifiedIntelligenceReady',result['actionStatus'])
        self.assertNotIn('No verified LAC-specific mandatory',result['answer'])
        ready=synthesize(CASE,NUMBER,[order([fact()])])
        result=answer(ready,CASE,'Latest Court direction',Never(),model_available=False)
        self.assertTrue(result['claims']);self.assertFalse(result['insufficientEvidence'])

    def test_stale_worker_file_is_not_busy_but_real_os_lock_is(self):
        from order_index import worker_busy,pdf_lock
        with tempfile.TemporaryDirectory()as root:
            folder=Path(root)/'court-intelligence/v1'
            with pdf_lock(folder):self.assertTrue(worker_busy(folder))
            self.assertFalse(worker_busy(folder))

    def test_lock_probe_failure_does_not_leave_runtime_permanently_busy(self):
        with tempfile.TemporaryDirectory()as root,patch('real_case.worker_busy',side_effect=PermissionError):
            c=RefreshController(root,lambda:object())
            with self.assertRaises(PermissionError):c.start(CASE,NUMBER,[source()])
            self.assertFalse(c.lock.locked());self.assertIsNone(c.active_case_id)

if __name__=='__main__':unittest.main()
