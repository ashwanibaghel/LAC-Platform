import json
import tempfile
import unittest
from pathlib import Path
from order_index import requested_date,merge_known_orders,prepare_question,index_entries,pdf_lock
from semantics import synthesize,identity
from questions import answer,ORDER_UNAVAILABLE,retrieve
from test_worker import fact,order
from test_questions import SelectAll

CASE='a1000000-0000-4000-8000-000000000012'
NUMBER='W.P.(C) 14604/2025'
URL='https://delhihighcourt.nic.in/app/showlogo/test.pdf/2026'

def source(**changes):
    result={'courtCaseId':CASE,'normalizedCaseIdentity':'delhihighcourt|wpc|14604|2025',
            'orderDate':'2026-07-29','officialUrl':URL,'sourceObservationId':'observation'}
    result.update(changes); return result

class OrderIndexTests(unittest.TestCase):
    def test_date_without_year_resolves_only_unique_actual_order(self):
        self.assertEqual('2026-07-29',requested_date('29 July wali hearing me kya hua?',['2026-07-29'])['date'])
        self.assertIsNone(requested_date('29 July wali hearing me kya hua?',['2025-07-29','2026-07-29'])['date'])
        self.assertEqual('2026-07-29',requested_date('What happened on 29 July 2026?',[])['date'])
        self.assertEqual('2026-04-16',requested_date('16.04.2026 affidavit ka kya hua?',[])['date'])
        self.assertFalse(requested_date('2025 se ab tak kya hua?',[])['requested'])

    def test_scheduled_date_without_actual_order_never_uses_other_date(self):
        artifact=synthesize(CASE,NUMBER,[order([fact('List on 01.10.2026.',field='nextHearing',actor=None,deadlineText=None)],orderDate='2026-07-29')])
        result=answer(artifact,CASE,'What happened on 1 October 2026?',SelectAll())
        self.assertEqual(ORDER_UNAVAILABLE,result['answer']); self.assertEqual([],result['claims'])
        self.assertEqual([],retrieve(artifact,'What happened on 1 October 2026?'))

    def test_exact_date_retrieval_uses_only_that_order(self):
        artifact=synthesize(CASE,NUMBER,[order([fact()],orderDate='2026-04-16'),order([fact()],orderDate='2026-07-29')])
        found=retrieve(artifact,'What happened on 16 April 2026?')
        self.assertEqual({'2026-04-16'},{entry['source']['orderDate'] for entry in found})

    def test_existing_observation_metadata_reused_without_html_hash(self):
        artifact=merge_known_orders(None,CASE,NUMBER,[source()])
        entry=artifact['orderIndex'][0]
        self.assertEqual('Unprocessed',entry['processingState'])
        self.assertEqual('observation',entry['sourceObservationId'])
        self.assertIsNone(entry['sourceSha256'])
        self.assertFalse(artifact['processingComplete'])

    def test_cross_case_or_other_forum_and_unsafe_url_excluded(self):
        for change in ({'courtCaseId':'different'},{'normalizedCaseIdentity':'supremecourt|wpc|14604|2025'},
                       {'officialUrl':'https://example.com/order.pdf'},{'officialUrl':URL+'?secret=a'}):
            self.assertEqual([],merge_known_orders(None,CASE,NUMBER,[source(**change)])['orderIndex'])

    def test_explicit_retry_processes_one_pdf_then_reuses_stored_intelligence(self):
        artifact=merge_known_orders(None,CASE,NUMBER,[source()]); calls=[]
        def processor(source,number,provider):
            calls.append(source['officialUrl'])
            return order([fact()],officialUrl=source['officialUrl'],orderDate=source['orderDate'],versions={'extraction':'test'})
        with tempfile.TemporaryDirectory() as root:
            updated=prepare_question(root,artifact,CASE,'Retry processing 29 July 2026.',object(),processor)
            again=prepare_question(root,updated,CASE,'29 July wali hearing me kya hua?',object(),processor)
            self.assertEqual([URL],calls)
            self.assertEqual('Processed',again['orderIndex'][0]['processingState'])
            self.assertEqual([],list(Path(root).rglob('*.pdf')))
            self.assertTrue((Path(root)/'court-intelligence'/'v1'/CASE/'current.json').is_file())

    def test_general_question_unknown_date_or_other_case_never_downloads(self):
        artifact=merge_known_orders(None,CASE,NUMBER,[source()])
        def forbidden(*args): self.fail('Unexpected PDF request')
        with tempfile.TemporaryDirectory() as root:
            for question in ['Poore matter ka summary batao','What happened on 1 October 2026?',
                             'What happened on 29 July 2026 in W.P.(C) 999/2025?','Compare all cases on 29 July 2026']:
                prepare_question(root,artifact,CASE,question,object(),forbidden)
            self.assertEqual([],list(Path(root).iterdir()))

    def test_review_sources_do_not_redownload_on_every_date_question(self):
        artifact=merge_known_orders(None,CASE,NUMBER,[source()])
        for status in ('NeedsReview','NeedsSourceReview'):
            artifact['orders'][0]['status']=status
            with tempfile.TemporaryDirectory() as root:
                prepare_question(root,artifact,CASE,'What happened on 29 July 2026?',object(),lambda *args:self.fail('Implicit retry'))

    def test_unreadable_pdf_and_changed_known_hash_fail_closed(self):
        for prior_sha in (None,'b'*64):
            artifact=merge_known_orders(None,CASE,NUMBER,[source()]); artifact['orders'][0]['sha256']=prior_sha
            def processor(*args):
                return order([fact()] if prior_sha else [],status='Validated' if prior_sha else 'NeedsSourceReview',officialUrl=URL,orderDate='2026-07-29')
            with tempfile.TemporaryDirectory() as root:
                result=prepare_question(root,artifact,CASE,'Retry processing 29 July 2026.',object(),processor)
                self.assertEqual('NeedsSourceReview',result['orders'][0]['status'])
                self.assertEqual([],result['orders'][0]['summaryFacts'])

    def test_full_story_preserves_origin_and_separate_party_voices(self):
        original=fact('The petition concerns refusal of reference.',category='CASE_CONTEXT',field='context',actor=None,deadlineText=None)
        petitioner=fact('The petitioner claims notice was not served.',category='PETITIONER_SUBMISSION',field='context',actor=None,deadlineText=None)
        lac=fact('The LAC submits notice was served.',category='LAC_OR_RESPONDENT_SUBMISSION',field='context',actor=None,deadlineText=None)
        artifact=synthesize(CASE,NUMBER,[order([original,petitioner],orderDate='2025-01-01'),order([lac],orderDate='2026-01-01')])
        found=retrieve(artifact,'Is petitioner ki poori kahani kya hai?')
        self.assertEqual({'CASE_CONTEXT','PETITIONER_SUBMISSION','LAC_OR_RESPONDENT_SUBMISSION'},{entry['category'] for entry in found})

    def test_full_story_origin_is_not_replaced_by_routine_bench_context(self):
        routine=fact('The Division Bench did not assemble.',category='CASE_CONTEXT',field='context',actor=None,deadlineText=None)
        dispute=fact('The present petition has been filed challenging the refusal letter.',category='CASE_CONTEXT',field='context',actor=None,deadlineText=None)
        artifact=synthesize(CASE,NUMBER,[order([routine],orderDate='2025-09-19'),order([dispute],orderDate='2026-04-16')])
        result=answer(artifact,CASE,'Is petitioner ki poori kahani kya hai?',SelectAll())
        self.assertIn('refusal letter',result['answer'])
        self.assertNotIn('did not assemble',result['answer'])

    def test_full_story_retains_substantive_party_stand_and_operative_direction(self):
        stand=fact('The LAC submits similar matters remain pending.',category='LAC_OR_RESPONDENT_SUBMISSION',field='context',actor=None,deadlineText=None)
        routine=fact('Adjournment is sought by counsel for the Respondents.',category='LAC_OR_RESPONDENT_SUBMISSION',field='context',actor=None,deadlineText=None)
        operative=fact('By way of last and final opportunity respondents shall file counter affidavit.',actor='Respondent',deadlineText=None)
        listing=fact('List on 01.10.2026.',field='nextHearing',actor=None,deadlineText=None)
        artifact=synthesize(CASE,NUMBER,[order([stand],orderDate='2026-04-16'),order([routine,operative,listing],orderDate='2026-07-29')])
        result=answer(artifact,CASE,'Poore matter ka summary batao.',SelectAll())
        self.assertIn('similar matters',result['answer']); self.assertIn('last and final',result['answer'])
        self.assertNotIn('Adjournment',result['answer']); self.assertNotIn('List on',result['answer'])

    def test_routine_latest_order_does_not_erase_substantive_history(self):
        substantive=order([fact('The petition concerns disputed compensation.',category='CASE_CONTEXT',field='context',actor=None,deadlineText=None)],orderDate='2025-01-01')
        routine=order([fact('List the matter on 01.10.2026.',field='nextHearing',actor=None,deadlineText=None)],orderDate='2026-07-29')
        artifact=synthesize(CASE,NUMBER,[substantive,routine])
        self.assertEqual('Routine',artifact['latestOrder']['presentationKind'])
        self.assertEqual('2025-01-01',artifact['latestMeaningfulOrder']['orderDate'])
        self.assertEqual(2,len(artifact['orders']))

    def test_old_disposition_then_later_orders_flags_continuity_not_current_disposal(self):
        disposed=order([fact('The writ petition is allowed.',category='DISPOSITION',field='disposition',actor=None,deadlineText=None)],orderDate='2015-10-05')
        routine=order([fact('List on 28.09.2026.',field='nextHearing',actor=None,deadlineText=None)],orderDate='2026-05-21')
        artifact=synthesize(CASE,NUMBER,[disposed,routine])
        self.assertIsNone(artifact['finalOrder']); self.assertTrue(artifact['chronologyWarnings'])

    def test_two_publications_on_same_date_do_not_guess_lazy_source(self):
        artifact=merge_known_orders(None,CASE,NUMBER,[source(),source(officialUrl=URL.replace('test','second'))])
        with tempfile.TemporaryDirectory() as root:
            prepare_question(root,artifact,CASE,'What happened on 29 July 2026?',object(),lambda *args:self.fail('Ambiguous source'))

if __name__=='__main__': unittest.main()
