"""Offline V2 boundary tests; no private workbook, HTTP or GPU needed."""
import copy
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent))
from audit_v2_foundation import audit_record, protected_matters, audit_relationships
from v2_candidates import CANDIDATES, FOLLOWUPS
from annotations.v2_review import REVIEWED


class V2SourceSafety(unittest.TestCase):
    def setUp(self):
        self.record = {'case': 'W.P.(C) 4806/2014', 'reserved_split': 'train',
                       'url': 'https://delhihighcourt.nic.in/app/showlogo/example.pdf/2026',
                       'sha256': 'a' * 64,
                       'pages': {'1': 'W.P.(C) 4806/2014 O R D E R % 07.02.2026 Let the LAC file an affidavit.'}}
        self.passages = [(1, 'Let the LAC file an affidavit.', 'COURT_DIRECTION',
                          'filing', 'Current', 'LAC', 'Explicit judicial direction.')]

    def check(self, record=None, date='2026-02-07', passages=None, protected=None):
        return audit_record(record or self.record, 'W.P.(C) 4806/2014', date,
                            self.passages if passages is None else passages,
                            protected_matters() if protected is None else protected)

    def test_bound_source_passes_without_gold_promotion(self):
        self.assertIn('W.P.(C) 4806/2014', self.check())
        self.assertNotIn('VERIFIED_GOLD', self.record.values())

    def test_listing_or_cited_date_is_not_document_date(self):
        self.record['pages']['1'] += ' List on 20.04.2026.'
        with self.assertRaisesRegex(ValueError, 'header date mismatch'):
            self.check(date='2026-04-20')

    def test_download_footer_digits_do_not_become_an_order_date(self):
        self.record['pages']['1'] += ' Downloaded at 20:45:49'
        self.record['pages']['2'] = 'September, 2020. Historical reference only.'
        self.assertIn('W.P.(C) 4806/2014', self.check())

    def test_case_cited_only_in_body_is_not_this_order_identity(self):
        self.record['pages']['1'] = self.record['pages']['1'].replace('W.P.(C) 4806/2014', 'W.P.(C) 123/2020')
        self.record['pages']['1'] += ' Earlier order in W.P.(C) 4806/2014.'
        with self.assertRaisesRegex(ValueError, 'source caption'):
            self.check()

    def test_wrong_page_or_changed_passage_rejected(self):
        for page, passage in [(2, self.passages[0][1]), (1, 'The affidavit was filed.')]:
            amended = [(page, passage, *self.passages[0][2:])]
            with self.assertRaisesRegex(ValueError, 'Unbound'):
                self.check(passages=amended)

    def test_explicit_review_reason_required(self):
        amended = [(*self.passages[0][:-1], '')]
        with self.assertRaisesRegex(ValueError, 'Unbound'):
            self.check(passages=amended)

    def test_reserved_eval_source_cannot_enter_training_review(self):
        for split in ['validation', 'blind']:
            amended = copy.deepcopy(self.record)
            amended['reserved_split'] = split
            with self.assertRaisesRegex(ValueError, 'split mismatch'):
                self.check(amended)

    def test_protected_matter_and_related_identity_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Protected source'):
            self.check(protected={'W.P.(C) 4806/2014'})
        for case in ['W.P.(C) 6384/2024', 'W.P.(C) 2686/2018', 'W.P.(C) 10105/2023']:
            amended = copy.deepcopy(self.record)
            amended['pages']['1'] += ' Connected ' + case
            with self.assertRaisesRegex(ValueError, 'Protected related'):
                self.check(amended)

    def test_official_https_and_binary_hash_required(self):
        for key, value in [('url', 'https://example.com/order.pdf'),
                           ('url', 'http://delhihighcourt.nic.in/order.pdf'),
                           ('sha256', 'not-a-hash')]:
            amended = copy.deepcopy(self.record)
            amended[key] = value
            with self.assertRaises(ValueError):
                self.check(amended)

    def test_review_only_contains_reserved_training_candidates(self):
        candidates = {key: (case, split) for key, case, split, _ in CANDIDATES + FOLLOWUPS}
        for version in REVIEWED:
            case, split = candidates[version]
            self.assertEqual(split, 'train')
            self.assertNotIn(case, protected_matters())

    def test_common_source_matters_remain_same_split(self):
        assignments = {case: split for _, case, split, _ in CANDIDATES + FOLLOWUPS}
        self.assertEqual(assignments['W.P.(C) 6108/2015'], assignments['W.P.(C) 4255/2016'])
        self.assertEqual(assignments['W.P.(C) 10105/2023'], 'blind')

    def test_connected_source_cannot_cross_split_even_with_different_sha(self):
        other = copy.deepcopy(self.record)
        other.update(case='W.P.(C) 123/2020', reserved_split='blind', sha256='b' * 64)
        other['pages']['1'] = 'W.P.(C) 123/2020 and W.P.(C) 4806/2014 O R D E R % 08.02.2026 List on 09.05.2026.'
        with self.assertRaisesRegex(ValueError, 'Protected split relationship'):
            audit_relationships({'first': self.record, 'other': other})

    def test_same_body_different_case_or_download_hash_cannot_cross_split(self):
        other = copy.deepcopy(self.record)
        other.update(case='W.P.(C) 123/2020', reserved_split='blind', sha256='b' * 64)
        other['pages']['1'] = other['pages']['1'].replace('4806/2014', '123/2020')
        with self.assertRaisesRegex(ValueError, 'Protected split relationship'):
            audit_relationships({'first': self.record, 'other': other})

    def test_common_family_is_recorded_without_reassigning_it(self):
        other = copy.deepcopy(self.record)
        other.update(case='W.P.(C) 123/2020', sha256='b' * 64)
        other['pages']['1'] = other['pages']['1'].replace('4806/2014', '123/2020')
        relations = audit_relationships({'first': self.record, 'other': other})
        self.assertTrue(relations[0]['near_duplicate_body'])
        self.assertEqual(other['reserved_split'], 'train')


if __name__ == '__main__':
    unittest.main()
