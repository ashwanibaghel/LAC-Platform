import unittest
import tempfile
from unittest.mock import patch
import requests
from query_intents import normalize
from questions import answer
from order_index import merge_known_orders
from semantics import identity
from test_questions import artifact
from test_worker import fact, order
from worker import process_order

class NoModel:
    version='test-no-inference'
    def extract(self,*args,**kwargs):
        raise AssertionError('Explicit officer action handling should not need model classification or facts')

class OfficerActionTests(unittest.TestCase):
    def test_official_download_timeout_is_source_failure_before_ai(self):
        class Session:
            trust_env=True
            closed=False
            def get(self,*args,**kwargs): raise requests.ReadTimeout('fixture')
            def close(self): self.closed=True
        session=Session()
        with tempfile.TemporaryDirectory() as folder, patch('worker.requests.Session',return_value=session):
            result=process_order({'orderDate':'2026-05-08','officialUrl':'https://delhihighcourt.nic.in/app/showlogo/order.pdf/2026'},'WPC NO. 6328/2026',NoModel(),folder)
        self.assertEqual('OfficialPdfDownloadTimeout',result['sourceReasonCode'])
        self.assertEqual('NeedsSourceReview',result['status'])
        self.assertEqual([],result['facts'])
        self.assertTrue(session.closed)
        self.assertFalse(session.trust_env)

    def test_all_officer_action_variants_are_deterministic(self):
        for question in ['what to do LAC Branch right now?','what should LAC do now?',
                         'what does LAC have to do?','what is required from LAC?',
                         'what action should LAC take?','next step for LAC?',
                         'LAC branch ko ab kya karna hai?','ab LAC kya kare?',
                         'hume ab kya karna hai?','अब हमें क्या करना है?','एल ए सी शाखा अब क्या करे?']:
            with self.subTest(question=question):
                self.assertIn('lac_action',normalize(question,NoModel())['topics'])

    def test_no_lac_directive_with_reviewed_sources_is_explicit_without_negative_inference(self):
        data=artifact()
        data['orders'].append(order([],status='NeedsSourceReview',orderDate='2026-02-01'))
        result=answer(data,'case-a','What to do LAC Branch Right now?',NoModel())
        self.assertEqual('LacActionNotEstablished',result['reason'])
        self.assertIn('currently processed evidence',result['answer'])
        self.assertIn('no conclusion is drawn from those sources',result['coverageNote'])
        self.assertEqual([],result['claims'])
        self.assertFalse(result['insufficientEvidence'])

    def test_zero_usable_evidence_and_metadata_only_blocked_sources(self):
        data={'caseId':'case-a','caseNumber':'WPC 1/2026','orders':[],'beforeNextHearing':[]}
        result=answer(data,'case-a','hume ab kya karna hai?',NoModel(),{'blockedBeforeAi':1,'pendingProcessing':0})
        self.assertEqual('NoUsableCourtEvidence',result['reason'])
        self.assertIn('No usable Court evidence',result['answer'])
        self.assertTrue(result['insufficientEvidence'])
        self.assertIn('source review',result['coverageNote'])

    def test_active_mandatory_action_returns_exact_cited_evidence(self):
        data=artifact()
        text=data['orders'][-1]['facts'][0]['value']
        data['beforeNextHearing']=[{'text':text}]
        result=answer(data,'case-a','what action should LAC take?',NoModel())
        self.assertEqual('VerifiedLacActions',result['reason'])
        self.assertEqual(text,result['claims'][0]['text'])
        self.assertEqual('2026-01-01',result['claims'][0]['source']['orderDate'])

    def test_display_no_marker_reaches_python_index_without_relaxing_case_identity(self):
        self.assertEqual(identity('WPC NO. 6328/2026'),identity('W.P.(C) 6328/2026'))
        source={'courtCaseId':'case-a','normalizedCaseIdentity':'delhihighcourt|wpc|6328|2026',
                'orderDate':'2026-05-08','officialUrl':'https://delhihighcourt.nic.in/app/showlogo/order.pdf/2026'}
        result=merge_known_orders(None,'case-a','WPC NO. 6328/2026',[source],strict_index=True)
        self.assertEqual(1,len(result['orders']))
        self.assertEqual([],merge_known_orders(None,'case-a','WPC NO. 6329/2026',[source],strict_index=True)['orders'])

if __name__=='__main__':unittest.main()
