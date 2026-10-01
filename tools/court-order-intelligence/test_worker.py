import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import fitz
from provider import LlamaCppProvider
from semantics import validate, due_date, office_action, synthesize
from worker import process_order, atomic_json, StopRequested

def fact(text='The LAC shall file a status report within four weeks from today.', **overrides):
    value = dict(category='COURT_DIRECTION', field='direction', value=text, actor='LAC',
                 deadlineText='within four weeks from today', scope='Current', page=1,
                 evidence=text, targetOrderDate=None, targetActionText=None)
    value.update(overrides)
    return value

def order(facts=None, **overrides):
    value = dict(officialUrl='https://delhihighcourt.nic.in/app/showFileJudgment/test.pdf',
                 orderDate='2026-01-01', sha256='a'*64, status='Validated', facts=facts or [])
    value.update(overrides)
    return value

class SafetyTests(unittest.TestCase):
    def test_complete_review_order_retains_only_verified_obligation(self):
        unclear=fact('An uncertain submission.',category='PETITIONER_SUBMISSION',scope='Uncertain',actor=None,deadlineText=None)
        output=synthesize('id','case',[order([fact(),unclear],status='NeedsReview',coverage={'allSelectedChunksProcessed':True})])
        self.assertEqual('NeedsReview',output['status'])
        self.assertEqual(1,len(output['beforeNextHearing']))
        self.assertEqual(1,len(output['latestOrder']['summaryFacts']))

    def test_partial_or_failed_review_order_creates_no_obligation(self):
        for extra in [{'coverage':{'allSelectedChunksProcessed':False}}, {'coverage':{'allSelectedChunksProcessed':True},'failureMessage':'Extraction failed'}]:
            output=synthesize('id','case',[order([fact()],status='NeedsReview',**extra)])
            self.assertEqual([],output['beforeNextHearing'])

    def run_native_fixture(self,caption,provider):
        with tempfile.TemporaryDirectory() as root:
            def downloaded(url,directory):
                path=Path(directory)/'order.pdf'
                with fitz.open() as document:
                    page=document.new_page()
                    page.insert_textbox(fitz.Rect(40,40,550,700),caption+' January 1, 2026 O R D E R '+('The LAC shall file a status report within four weeks from today. '*4))
                    document.save(path)
                return path,'a'*64
            result=process_order({'officialUrl':'url','orderDate':'2026-01-01'},'W.P.(C) 1/2026',provider,root,downloaded)
            self.assertEqual([],list(Path(root).iterdir()))
            return result

    def test_connected_case_pdf_does_not_leak_other_matter_facts(self):
        class Never:
            version='test'
            def extract(self,*args): raise AssertionError('Mixed case evidence reached inference')
        result=self.run_native_fixture('W.P.(C) 1/2026 and W.P.(C) 99/2026',Never())
        self.assertEqual('NeedsSourceReview',result['status'])
        self.assertEqual([],result['facts'])
        self.assertIn('case-specific attribution',result['failureMessage'])

    def test_case_mentioned_only_in_body_is_not_caption_identity(self):
        class Never:
            version='test'
            def extract(self,*args): raise AssertionError('Wrong case source reached inference')
        result=self.run_native_fixture('W.P.(C) 99/2026 O R D E R Referring to W.P.(C) 1/2026:',Never())
        self.assertEqual('NeedsReview',result['status'])
        self.assertIn('absent from Court caption',result['failureMessage'])

    def test_successful_native_processing_keeps_identity_and_cleans_pdf(self):
        class Selected:
            version='test'
            def extract(self,instructions,source,schema,feedback=''):
                anchor=json.loads(source)['anchors'][0]
                return {'facts':[{'anchorId':anchor['anchorId'],'category':'COURT_DIRECTION','field':'direction','scope':'Current'}],'needsReview':False}
        result=self.run_native_fixture('IN THE HIGH COURT OF DELHI W.P.(C) 1/2026 CORAM: JUSTICE EXAMPLE',Selected())
        self.assertEqual('Validated',result['status'])
        self.assertEqual('W.P.(C) 1/2026',result['rawIdentity'])
        self.assertEqual(1,result['identityPage'])
        self.assertEqual('Delhi High Court',result['court'])
        self.assertIn('JUSTICE EXAMPLE',result['bench'])

    def test_submission_cannot_be_direction(self):
        for actor in ['The petitioner', 'Counsel for LAC']:
            text = actor + ' submits that the LAC shall file a report.'
            with self.assertRaises(ValueError):
                validate({'facts': [fact(text, deadlineText=None)], 'needsReview': False}, {1:text})

    def test_submission_retains_attribution(self):
        text = 'The LAC submits compensation has been paid.'
        validated = validate({'facts':[fact(text, category='LAC_OR_RESPONDENT_SUBMISSION', field='compensation', deadlineText=None)], 'needsReview':False}, {1:text})
        self.assertIsNone(office_action(validated['facts'][0], order()))

    def test_party_claim_cannot_become_court_finding(self):
        text='The petitioner submits compensation was never paid.'
        with self.assertRaises(ValueError):
            validate({'facts':[fact(text,category='COURT_FINDING',field='compensation',actor=None,deadlineText=None)],'needsReview':False},{1:text})

    def test_material_negation_cannot_be_omitted(self):
        text='The LAC shall not release compensation.'
        with self.assertRaises(ValueError):
            validate({'facts':[fact(text,value='release compensation.',deadlineText=None)],'needsReview':False},{1:text})

    def test_scanned_pdf_never_invokes_model_and_is_cleaned(self):
        class Never:
            version='test'
            def extract(self,*args): raise AssertionError('Scanned source reached inference')
        with tempfile.TemporaryDirectory() as root:
            def downloaded(url,directory):
                path=Path(directory)/'order.pdf'
                with fitz.open() as document:
                    document.new_page(); document.save(path)
                return path,'a'*64
            result=process_order({'officialUrl':'url','orderDate':'2026-01-01'},'case',Never(),root,downloaded)
            self.assertEqual('NeedsSourceReview',result['status'])
            self.assertEqual([],result['facts'])
            self.assertEqual([],list(Path(root).iterdir()))

    def test_land_facts_require_source_values(self):
        for field in ['village','khasra','award','compensation','possession']:
            text = 'Village Bijwasan Khasra 12 Award 7 compensation unpaid; possession taken.'
            with self.assertRaises(ValueError):
                validate({'facts':[fact(text, field=field, value='invented', actor=None, deadlineText=None)], 'needsReview':False}, {1:text})

    def test_wrong_page_rejected(self):
        entry = fact()
        with self.assertRaises(ValueError):
            validate({'facts':[entry], 'needsReview':False}, {2:entry['evidence']})

    def test_relative_dates(self):
        self.assertEqual('2026-01-29', due_date('2026-01-01','within four weeks from today'))
        self.assertEqual('2026-02-28', due_date('2026-01-31','within one month from this order'))
        self.assertIsNone(due_date('2026-01-01','within six months thereafter'))
        self.assertIsNone(due_date('2026-01-01','within four weeks from receipt'))

    def test_next_hearing_no_invented_date(self):
        self.assertIsNone(due_date('2026-01-01','before next hearing'))
        self.assertEqual('2026-03-01', due_date('2026-01-01','before next hearing','2026-03-01'))

    def test_other_party_and_jurisdiction_no_office_task(self):
        for actor in ['DDA','Petitioner','LAC Faridabad','respondent 5',None]:
            self.assertIsNone(office_action(fact(actor=actor), order()))

    def test_quoted_old_direction_no_office_task(self):
        self.assertIsNone(office_action(fact(scope='Quoted'), order()))

    def test_silence_does_not_close(self):
        output = synthesize('id','case',[order([fact()]),order(orderDate='2026-02-01', sha256='b'*64)])
        self.assertEqual('Not confirmed complete',output['actions'][0]['state'])

    def test_explicit_linked_compliance_closes(self):
        action = fact()
        text = 'The direction dated 01.01.2026: ' + action['value'] + ' has been complied with; the status report was filed.'
        completion = fact(text, field='compliance', category='COURT_FINDING', deadlineText=None,
                          targetOrderDate='2026-01-01', targetActionText=action['value'])
        output = synthesize('id','case',[order([action]),order([completion],orderDate='2026-02-01',sha256='b'*64)])
        self.assertEqual('Completed',output['actions'][0]['state'])
        self.assertEqual([],output['beforeNextHearing'])

    def test_unlinked_compliance_does_not_close(self):
        text = 'The status report was filed.'
        completion = fact(text, category='COURT_FINDING',field='compliance',actor=None,deadlineText=None)
        output = synthesize('id','case',[order([fact()]),order([completion],orderDate='2026-02-01',sha256='b'*64)])
        self.assertEqual(1,len(output['beforeNextHearing']))

    def test_explicit_supersession_requires_original_date_and_action(self):
        original=fact()
        text='The direction dated 01.01.2026: '+original['value']+' is expressly superseded.'
        replaced=fact(text,category='COURT_FINDING',field='supersession',actor=None,deadlineText=None)
        output=synthesize('id','case',[order([original]),order([replaced],orderDate='2026-02-01',sha256='b'*64)])
        self.assertEqual('Superseded',output['actions'][0]['state'])
        self.assertEqual([],output['beforeNextHearing'])

    def test_cooperative_stop_cleans_inflight_temporary_source(self):
        class Never: version='test'
        with tempfile.TemporaryDirectory() as root:
            marker=Path(root)/'stop'
            def downloader(url,directory):
                path=Path(directory)/'partial.pdf';path.write_bytes(b'partial')
                marker.touch()
                return path,'a'*64
            with self.assertRaises(StopRequested):
                process_order({'officialUrl':'url','orderDate':'2026-01-01'},'case',Never(),root,downloader,str(marker))
            self.assertEqual([marker],list(Path(root).iterdir()))

    def test_contradiction_needs_review(self):
        texts = ['Compensation was paid.','Compensation was not paid.']
        facts = [fact(text,category='LAC_OR_RESPONDENT_SUBMISSION',field='compensation',actor=None,deadlineText=None) for text in texts]
        self.assertTrue(validate({'facts':facts,'needsReview':False},{1:' '.join(texts)})['needsReview'])

    def test_petition_filing_is_not_recorded_compliance(self):
        text='The petitioner filed this petition under Article 226.'
        candidate=fact(text,category='PROCEDURAL_EVENT',field='compliance',actor=None,deadlineText=None)
        with self.assertRaisesRegex(ValueError,'explicit obligation'):
            validate({'facts':[candidate],'needsReview':False},{1:text})

    def test_loopback_only(self):
        for endpoint in ['https://example.org','http://localhost:8096','http://127.0.0.1.evil:8096','http://user:pass@127.0.0.1:8096','http://127.0.0.1:8096/path']:
            with self.assertRaises(ValueError): LlamaCppProvider(endpoint)
        self.assertFalse(LlamaCppProvider().session.trust_env)

    def test_latest_unreadable_not_presented_as_current(self):
        output = synthesize('id','case',[order(),order(status='NeedsSourceReview',orderDate='2026-02-01')])
        self.assertEqual('NeedsReview',output['status'])
        self.assertEqual([],output['currentPosition'])

    def test_cleanup_and_retry_failure_closed(self):
        class Broken:
            version='test'
            calls=0
            def extract(self,*args):
                self.calls += 1
                return {'malformed':True}
        provider=Broken()
        with tempfile.TemporaryDirectory() as root:
            def downloader(url,directory):
                path=Path(directory)/'order.pdf'
                with fitz.open() as document:
                    page=document.new_page()
                    page.insert_textbox(fitz.Rect(40,40,550,700),'W.P.(C) 1/2026 January 1, 2026 O R D E R '+('The LAC shall file a status report. '*8))
                    document.save(path)
                return path,'a'*64
            result=process_order({'officialUrl':'https://delhihighcourt.nic.in/test.pdf','orderDate':'2026-01-01'},'W.P.(C) 1/2026',provider,root,downloader)
            self.assertEqual('NeedsReview',result['status'])
            self.assertEqual([],result['facts'])
            self.assertEqual(2,provider.calls)
            self.assertEqual([],list(Path(root).iterdir()))

    def test_cleanup_source_failure(self):
        class Local: version='test'
        with tempfile.TemporaryDirectory() as root:
            def downloader(url,directory):
                (Path(directory)/'partial.pdf').write_bytes(b'partial')
                raise ValueError('Official direct PDF unavailable')
            process_order({'officialUrl':'url','orderDate':'2026-01-01'},'case',Local(),root,downloader)
            self.assertEqual([],list(Path(root).iterdir()))

    def test_worker_has_no_database_or_canonical_mutation(self):
        source=Path(__file__).with_name('worker.py').read_text()
        for forbidden in ['psycopg','Npgsql','CourtCase.CurrentStatus','CourtProceeding','MigrateAsync','SeedAsync']:
            self.assertNotIn(forbidden,source)

    def test_atomic_artifact(self):
        with tempfile.TemporaryDirectory() as root:
            path=Path(root)/'current.json'
            atomic_json(path,{'version':1})
            self.assertEqual({'version':1},json.loads(path.read_text()))
            self.assertEqual([path],list(Path(root).iterdir()))

if __name__ == '__main__': unittest.main()
