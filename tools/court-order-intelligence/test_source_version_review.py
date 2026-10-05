import tempfile,hashlib,json,unittest,shutil
from pathlib import Path
import fitz
from worker import process_order
from source_version_review import review_cached_selections

class SourceReviewTests(unittest.TestCase):
    def fixture(self,root,extra=''):
        p=Path(root)/'cached.pdf'
        with fitz.open()as doc:
            page=doc.new_page()
            text=('IN THE HIGH COURT OF DELHI\nW.P.(C) 1/2026\nCORAM: JUSTICE EXAMPLE\nJanuary 1, 2026\nO R D E R\n'
                '1. The LAC shall file a detailed status report concerning compensation and land possession within four weeks from today.\n'+extra)
            page.insert_textbox(fitz.Rect(40,40,550,700),text);doc.save(p)
        return p
    def prior(self,path):
        class All:
            version='test'
            def extract(self,instructions,prompt,schema,feedback=''):
                return dict(facts=[dict(anchorId=a['anchorId'],category='COURT_DIRECTION',field='direction',scope='Current')
                    for a in json.loads(prompt)['anchors']],needsReview=False)
        source=dict(officialUrl='https://delhihighcourt.nic.in/app/showlogo/test.pdf/2026',orderDate='2026-01-01',
            courtCaseId='test-case',sourceEvidenceSha256='row-hash')
        def local(url,temp):
            p=Path(temp)/'order.pdf';shutil.copyfile(path,p);return p,hashlib.sha256(path.read_bytes()).hexdigest()
        prior=process_order(source,'W.P.(C) 1/2026',All(),temporary_root=path.parent,downloader=local)
        prior.update(courtCaseId='test-case',processedEvidenceSha256='row-hash')
        self.assertFalse(prior.get('failureMessage'));self.assertTrue(prior['coverage']['allSelectedChunksProcessed'])
        return source,prior
    def test_changed_bytes_identical_full_native_evidence_revalidate_without_inference(self):
        with tempfile.TemporaryDirectory()as root:
            p=self.fixture(root);source,prior=self.prior(p)
            with fitz.open(p)as doc:
                doc.set_metadata({'title':'Changed PDF container only'});new=Path(root)/'new.pdf';doc.save(new)
            sha=hashlib.sha256(new.read_bytes()).hexdigest();self.assertNotEqual(prior['sha256'],sha)
            record=review_cached_selections(source,'W.P.(C) 1/2026',prior,new,sha,'test')
            self.assertEqual('Validated',record['status']);self.assertEqual(prior['facts'],record['facts'])
            self.assertEqual(0,record['sourceVersionReview']['modelCalls'])
    def test_new_native_direction_cannot_be_silently_covered_by_cached_classification(self):
        with tempfile.TemporaryDirectory()as root:
            p=self.fixture(root);source,prior=self.prior(p);p.unlink()
            p=self.fixture(root,'2. The LAC shall also produce the acquisition records before this Court.\n')
            with self.assertRaisesRegex(ValueError,'coverage'):review_cached_selections(source,'W.P.(C) 1/2026',prior,p,hashlib.sha256(p.read_bytes()).hexdigest(),'test')
    def test_changed_row_publication_and_wrong_pdf_hash_fail_closed(self):
        with tempfile.TemporaryDirectory()as root:
            p=self.fixture(root);source,prior=self.prior(p)
            with self.assertRaisesRegex(ValueError,'hash mismatch'):review_cached_selections(source,'W.P.(C) 1/2026',prior,p,'0'*64,'test')
            source['sourceEvidenceSha256']='new-row'
            with self.assertRaisesRegex(ValueError,'provenance'):review_cached_selections(source,'W.P.(C) 1/2026',prior,p,prior['sha256'],'test')

if __name__=='__main__':unittest.main()
