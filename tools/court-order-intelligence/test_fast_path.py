import json,tempfile,unittest
from pathlib import Path
from unittest.mock import patch
from fast_path import select_fast_candidates,compact_prompt
from real_case import refresh_case,read_artifact
from semantics import VERSION,synthesize
from worker import atomic_json,process_order,StopRequested
from order_index import merge_known_orders,prepare_question
from questions import answer
from test_real_case import CASE,NUMBER,URL,source,write_existing
from test_worker import fact,order

VERSIONS={'extraction':VERSION,'rulebook':'2','model':'fixture'}
class Never:
    version='fixture'
    def extract(self,*args,**kwargs):raise AssertionError('Unexpected inference')

def cached(src, status='Validated'):
    return order([fact()],officialUrl=src['officialUrl'],orderDate=src['orderDate'],status=status,
        coverage={'allSelectedChunksProcessed':True},versions=VERSIONS,courtCaseId=CASE,
        caseNumber=NUMBER,sourceObservationId=src['sourceObservationId'],processedObservationId=src['sourceObservationId'])

class FastPathTests(unittest.TestCase):
    def test_eight_old_orders_plus_new_latest_process_latest_first_and_display_chronologically(self):
        sources=[source(day=f'2026-01-{day:02d}',url=URL.replace('fixture',str(day))) for day in range(1,10)]
        seen=[];snapshots=[]
        def process(src,*args):
            seen.append(src['orderDate']);return cached(src)
        with tempfile.TemporaryDirectory() as root:
            result,_=refresh_case(root,CASE,NUMBER,sources,Never(),processor=process,
                progress=lambda *args:snapshots.append(read_artifact(root,CASE)))
        self.assertEqual('2026-01-09',seen[0]);self.assertEqual(sorted(seen,reverse=True),seen)
        self.assertEqual(sorted(seen),[o['orderDate'] for o in result['orders']])
        self.assertFalse(snapshots[0]['processingComplete']);self.assertEqual(1,len([o for o in snapshots[0]['orders'] if o['status']=='Validated']))
        self.assertTrue(snapshots[-1]['processingComplete'])

    def test_nineteen_unchanged_orders_including_reviewed_skip_only_new_source_processes(self):
        sources=[source(day=f'2026-01-{day:02d}',url=URL.replace('fixture',str(day))) for day in range(1,21)]
        old=[cached(src,'NeedsReview' if i==0 else 'Validated') for i,src in enumerate(sources[:-1])]
        with tempfile.TemporaryDirectory() as root:
            write_existing(root,old);seen=[]
            result,_=refresh_case(root,CASE,NUMBER,sources,Never(),processor=lambda src,*args:seen.append(src['orderDate']) or cached(src))
            self.assertEqual(['2026-01-20'],seen)
            self.assertEqual([r['facts'] for r in old],[r['facts'] for r in result['orders'][:-1]])
            refresh_case(root,CASE,NUMBER,sources,Never(),processor=lambda *args:self.fail('Cached reprocessing'))

    def test_unchanged_source_block_does_not_download_or_use_model(self):
        src=source();record=cached(src,'NeedsSourceReview');record.update(facts=[],failureMessage='NeedsSourceReview: connected-case PDF',sourceReasonCode='ConnectedCasePdf')
        with tempfile.TemporaryDirectory() as root:
            write_existing(root,[record])
            result,reviews=refresh_case(root,CASE,NUMBER,[src],Never(),processor=lambda *args:self.fail('Blocked source processor'))
        self.assertEqual(1,reviews);self.assertEqual([],result['orders'][0]['summaryFacts'])

    def test_fast_callback_atomically_publishes_latest_before_processor_and_history_finish(self):
        sources=[source(),source(day='2026-02-01',url=URL.replace('fixture','new'))]
        snapshots=[]
        with tempfile.TemporaryDirectory() as root:
            def process(src,number,provider,fast=False,on_fast_ready=None):
                record=cached(src)
                if fast:
                    partial=dict(record,status='NeedsReview',briefTier='Fast',deepProcessingComplete=False)
                    on_fast_ready(partial)
                    snapshots.append(read_artifact(root,CASE))
                return dict(record,briefTier='Deep',deepProcessingComplete=True)
            with patch('real_case.process_order',process):
                result,_=refresh_case(root,CASE,NUMBER,sources,Never(),processor=process)
        self.assertFalse(snapshots[0]['processingComplete'])
        self.assertEqual('2026-02-01',snapshots[0]['latestOrder']['orderDate'])
        self.assertTrue(snapshots[0]['latestOrder']['summaryFacts'])
        self.assertEqual('Unprocessed',snapshots[0]['orders'][0]['status'])
        self.assertTrue(result['processingComplete'])

    def test_other_case_inference_busy_does_not_claim_this_cases_history_is_processing(self):
        artifact=synthesize(CASE,NUMBER,[cached(source())])
        result=answer(artifact,CASE,'Summarize the latest order.',Never(),inference_busy=True)
        self.assertTrue(result['claims']);self.assertNotIn('coverageNote',result)

    def test_changed_publication_model_or_semantic_version_invalidates_cache(self):
        for key,value in [('processedEvidenceSha256','old'),('versions',dict(VERSIONS,model='old')),('versions',dict(VERSIONS,extraction='old'))]:
            src=source()
            src['sourceEvidenceSha256']='new'
            record=cached(src);record['processedEvidenceSha256']='new';record[key]=value
            with tempfile.TemporaryDirectory() as root:
                write_existing(root,[record]);seen=[]
                refresh_case(root,CASE,NUMBER,[src],Never(),processor=lambda src,*args:seen.append(src) or cached(src))
                self.assertEqual(1,len(seen))

    def test_interrupted_background_resumes_remaining_and_preserves_latest(self):
        sources=[source(day=f'2026-01-{day:02d}',url=URL.replace('fixture',str(day))) for day in range(1,4)]
        seen=[]
        def interrupt(src,*args):
            seen.append(src['orderDate'])
            if len(seen)==2:raise StopRequested()
            return cached(src)
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(StopRequested):refresh_case(root,CASE,NUMBER,sources,Never(),processor=interrupt)
            saved=read_artifact(root,CASE);self.assertFalse(saved['processingComplete'])
            resumed=[]
            result,_=refresh_case(root,CASE,NUMBER,sources,Never(),processor=lambda src,*args:resumed.append(src['orderDate']) or cached(src))
            self.assertEqual(['2026-01-02','2026-01-01'],resumed)
            self.assertEqual(saved['orders'][-1]['facts'],result['orders'][-1]['facts'])

    def test_fast_selection_never_truncates_operative_anchor_overflow_or_quote_metadata(self):
        anchors=[dict(anchorId=i,page=1,text=f'The LAC shall file document {i}.',quoted=False,actors=['LAC']) for i in range(9)]
        selected=select_fast_candidates(anchors)
        self.assertEqual(anchors,selected)
        quoted=dict(anchorId=10,page=2,text='The LAC shall release compensation.',quoted=True,speechRole=None,evidenceParts=[{'page':2,'text':'exact'}])
        prompt=json.loads(compact_prompt('2026-01-01',NUMBER,[quoted]))
        self.assertTrue(prompt['anchors'][0]['quoted']);self.assertEqual([],prompt['operativeAnchorIds'])
        self.assertEqual(quoted['evidenceParts'],prompt['anchors'][0]['evidenceParts'])

    def test_partial_coverage_qa_is_immediate_cited_and_pending_date_has_no_claim(self):
        ready=cached(source(day='2026-02-01'))
        ready.update(briefTier='Fast',deepProcessingComplete=False)
        pending=source(day='2026-01-01',url=URL.replace('fixture','older'))
        artifact=merge_known_orders(synthesize(CASE,NUMBER,[ready]),CASE,NUMBER,[source(day='2026-02-01'),pending])
        result=answer(artifact,CASE,'What should LAC do now?',Never(),background_processing=True)
        self.assertTrue(result['claims']);self.assertIn('awaiting AI processing',result['coverageNote'])
        summary=answer(artifact,CASE,'Summarize the latest order.',Never(),background_processing=True)
        self.assertTrue(summary['claims']);self.assertIn('currently verified',summary['coverageNote'])
        missing=answer(artifact,CASE,'What happened on 1 January 2026?',Never(),background_processing=True)
        self.assertEqual('OrderProcessing',missing['reason']);self.assertEqual([],missing['claims'])
        with tempfile.TemporaryDirectory() as root:
            prepare_question(root,artifact,CASE,'What happened on 1 January 2026?',Never(),lambda *args:self.fail('Implicit PDF fetch'))

    def test_unresolved_operative_evidence_does_not_publish_a_context_only_fast_brief(self):
        texts=['The petition concerns compensation.','The LAC shall file a status report.']
        pages={1:'IN THE HIGH COURT OF DELHI W.P.(C) 42/2026 January 1, 2026 O R D E R '+' '.join(texts)}
        anchors=[dict(anchorId=i,page=1,text=text,actors=['LAC'] if i else [],quoted=False) for i,text in enumerate(texts)]
        class ContextOnly:
            version='fixture'
            def extract(self,instructions,prompt,schema,feedback=''):
                return {'facts':[dict(anchorId=0,category='CASE_CONTEXT',field='context',scope='Current')
                    for a in json.loads(prompt)['anchors'] if a['anchorId']==0],'needsReview':False}
        published=[]
        with tempfile.TemporaryDirectory() as root:
            def download(url,directory):
                path=Path(directory)/'order.pdf';path.write_bytes(b'%PDF-');return path,'a'*64
            with patch('worker.native_pages',return_value=pages),patch('worker.outer_paragraph_offsets',return_value={}),patch('worker.anchors_for',return_value=anchors):
                result=process_order(source(),NUMBER,ContextOnly(),root,download,fast=True,on_fast_ready=published.append)
        self.assertEqual([],published)
        self.assertEqual('NeedsReview',result['status'])
        self.assertTrue(result['coverage']['completenessRetry']['remainingAnchorIds'])

    def test_fast_worker_publishes_selected_batch_before_background_and_cleans_one_pdf(self):
        texts=[f'The petition concerns compensation dispute number {i}.' for i in range(8)]+['The LAC shall file a status report.']
        pages={1:'IN THE HIGH COURT OF DELHI W.P.(C) 42/2026 January 1, 2026 O R D E R '+' '.join(texts)}
        anchors=[dict(anchorId=i,page=1,text=text,actors=['LAC'] if i==8 else [],quoted=False) for i,text in enumerate(texts)]
        class Select:
            version='fixture'
            calls=0
            def extract(self,instructions,prompt,schema,feedback=''):
                self.calls+=1
                return {'facts':[dict(anchorId=a['anchorId'],category='COURT_DIRECTION' if a['anchorId']==8 else 'CASE_CONTEXT',field='direction' if a['anchorId']==8 else 'context',scope='Current') for a in json.loads(prompt)['anchors']],'needsReview':False}
        provider=Select();published=[]
        with tempfile.TemporaryDirectory() as root:
            def download(url,directory):
                path=Path(directory)/'order.pdf';path.write_bytes(b'%PDF-');return path,'a'*64
            with patch('worker.native_pages',return_value=pages),patch('worker.outer_paragraph_offsets',return_value={}),patch('worker.anchors_for',return_value=anchors):
                result=process_order(source(),NUMBER,provider,root,download,fast=True,on_fast_ready=lambda r:published.append((r,provider.calls)))
            self.assertEqual([],list(Path(root).iterdir()))
        self.assertEqual(1,published[0][1]);self.assertFalse(published[0][0]['deepProcessingComplete'])
        self.assertIn('The LAC shall file a status report.',[f['evidence'] for f in published[0][0]['facts']])
        self.assertTrue(result['deepProcessingComplete']);self.assertEqual(9,len(result['facts']));self.assertEqual(2,provider.calls)
        # An interrupted enrichment resumes from the complete fast batch and
        # applies the same independent source/page/role guards to cached facts.
        resumed=Select();resume_publications=[]
        with tempfile.TemporaryDirectory() as root:
            with patch('worker.native_pages',return_value=pages),patch('worker.outer_paragraph_offsets',return_value={}),patch('worker.anchors_for',return_value=anchors):
                completed=process_order(published[0][0],NUMBER,resumed,root,download,fast=True,
                    on_fast_ready=lambda r:resume_publications.append(resumed.calls))
        self.assertEqual([0],resume_publications);self.assertEqual(1,resumed.calls)
        self.assertEqual(9,len(completed['facts']))

if __name__=='__main__':unittest.main()
