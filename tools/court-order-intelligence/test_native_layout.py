import tempfile
import unittest
from pathlib import Path
import fitz
from anchors import anchors_for, expand
from native_layout import outer_paragraph_offsets
from worker import native_pages


class NativeBoundaryTests(unittest.TestCase):
    def layout(self, labels):
        directory = tempfile.TemporaryDirectory()
        path = Path(directory.name)/'source.pdf'
        with fitz.open() as doc:
            page = doc.new_page()
            page.insert_text((60, 50), 'O R D E R')
            for index, (number, x) in enumerate(labels):
                y = 100+index*55
                page.insert_text((x, y), str(number)+'.')
                page.insert_text((x+30, y), 'The compensation remains disputed in the records.')
            doc.save(path)
        return directory, path, native_pages(path)

    def test_only_consecutive_outer_margin_numbers_certify_resets(self):
        directory, path, pages = self.layout([(1, 60), (2, 60), (10, 130), (11, 130), (3, 60)])
        try:
            result = outer_paragraph_offsets(path, pages)
            self.assertEqual([1, 2, 3], [number for _, number in result[1]])
        finally: directory.cleanup()

    def test_ambiguous_left_aligned_quoted_numbering_has_no_certificate(self):
        directory, path, pages = self.layout([(1, 60), (2, 60), (10, 60), (11, 60), (3, 60)])
        try: self.assertEqual({}, outer_paragraph_offsets(path, pages))
        finally: directory.cleanup()

    def test_duplicate_numbering_fails_closed(self):
        directory, path, pages = self.layout([(1, 60), (2, 60), (2, 60), (3, 60)])
        try: self.assertEqual({}, outer_paragraph_offsets(path, pages))
        finally: directory.cleanup()

    def test_native_return_resets_leaked_nested_quote_not_indented_quote(self):
        text = '1. The earlier order reads: “The compensation report reads: “Compensation is disputed. The LAC shall file a report.” 2. The Court directs the LAC to examine documents. 3. The petition is disposed of.'
        pages = {1: text}
        boundaries = {1: [(text.index('1.'), 1), (text.index('2.'), 2), (text.index('3.'), 3)]}
        anchors = anchors_for(pages, boundaries)
        old = next(a for a in anchors if 'shall file' in a['text'])
        fresh = next(a for a in anchors if 'examine' in a['text'])
        self.assertTrue(old['quoted'])
        self.assertFalse(fresh['quoted'])
        with self.assertRaisesRegex(ValueError, 'source quotation'):
            expand({'facts': [{'anchorId': old['anchorId'], 'category': 'COURT_DIRECTION', 'field': 'filing', 'scope': 'Current'}], 'needsReview': False}, anchors, pages)

    def test_nested_petitioner_claim_does_not_replace_explicit_lac_speaker(self):
        pages = {1: 'The stand of the LAC is that the petitioner claims ownership but title remains disputed. The additional affidavit filed by the LAC reads as under: “The petitioner claims to have purchased the land. Compensation has not been paid.”'}
        anchors = anchors_for(pages)
        self.assertTrue(all(a['speechRole'] == 'LAC_OR_RESPONDENT_SUBMISSION' for a in anchors))

    def test_native_court_return_clears_affidavit_speaker(self):
        text = '1. The stand of the LAC is that the petitioner claims ownership. 2. Therefore, no compensation has been paid. 3. The petition is disposed of.'
        anchors = anchors_for({1: text}, {1: [(text.index('1.'), 1), (text.index('2.'), 2), (text.index('3.'), 3)]})
        self.assertEqual('LAC_OR_RESPONDENT_SUBMISSION', anchors[0]['speechRole'])
        self.assertIsNone(next(a for a in anchors if 'Therefore' in a['text'])['speechRole'])


if __name__ == '__main__': unittest.main()
