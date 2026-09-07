import unittest

from benchmark.targeted_ocr_v4 import exact_consensus
from benchmark.domain_validate_v3 import AREA, reconstruct


class TargetedRecoveryTests(unittest.TestCase):
    def test_ocr_consensus_agreement(self):
        self.assertEqual(exact_consensus([{'texts':['22//2']},{'texts':['22//2']},{'texts':['22//2']}]), '22//2')

    def test_ocr_disagreement_stays_review(self):
        self.assertIsNone(exact_consensus([{'texts':['22//2']},{'texts':['22//7']},{'texts':['22//2']}]))

    def test_area_is_not_identifier_and_recorded_awarded_stay_separate(self):
        self.assertTrue(AREA.fullmatch('0 -- 14'))
        self.assertNotEqual(reconstruct('22','2')[0], '0 -- 14')

    def test_continuation_requires_explicit_rectangle(self):
        self.assertEqual(reconstruct('', '2')[1], 'Unreadable')


if __name__ == '__main__':
    unittest.main()
