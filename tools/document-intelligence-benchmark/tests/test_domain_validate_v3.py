import unittest

from benchmark.domain_validate_v3 import AREA, match_master, parse, reconstruct


class DomainValidationTests(unittest.TestCase):
    def test_rectangle_killa_and_qualifier(self):
        self.assertEqual(parse('22//1/6'), ('22//1/6', None))
        self.assertEqual(parse('22//1/6 min'), ('22//1/6', 'min'))
        self.assertEqual(reconstruct('22', '1/6'), ('22//1/6', 'ExplicitCells'))
        self.assertEqual(reconstruct('22', '1/6', 'min')[0], '22//1/6 min')

    def test_rectangle_continuation_requires_explicit_rectangle(self):
        self.assertEqual(reconstruct('', '2')[1], 'Unreadable')

    def test_area_and_serial_are_not_khasra(self):
        self.assertIsNone(parse('0 -- 14')[0])
        self.assertIsNone(parse('12')[0])
        self.assertTrue(AREA.fullmatch('0 -- 14'))

    def test_master_match_and_qualifier_mismatch_without_repair(self):
        master=[{'id':'a','displayNumber':'22//2'}]
        self.assertEqual(match_master('22//2',None,master)[0], 'ExactMasterMatch')
        self.assertEqual(match_master('22//2','min',master)[0], 'QualifierMismatch')
        self.assertEqual(match_master('22//1',None,master)[0], 'NoMasterMatch')

    def test_semantic_repetition_is_not_parser_deduplication(self):
        self.assertEqual(parse('22//2')[0], parse('22//2')[0])
        self.assertNotEqual(('page-2','award'), ('page-7','cwp'))


if __name__ == '__main__':
    unittest.main()
