import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmark.worker_semantics import award_candidate, court_candidate, field, strict_khasra, table_kind


def cell(text, x=0):
    return {"text": text, "region": {"x": x, "y": 10, "width": 20, "height": 10}, "confidence": .9}


class WorkerSemanticsTests(unittest.TestCase):
    def setUp(self):
        self.roles = {"rectangle": 0, "khasra": 1, "recordedArea": 2, "awardedArea": 3}
        self.cells = {0: cell("1", 0), 1: cell("22//2/1 min", 20), 2: cell("22 -- 3", 40), 3: cell("20-1", 60)}

    def test_numeric_narrative_never_becomes_award_khasra(self):
        cells = dict(self.cells); cells[1] = cell("2002", 20)
        self.assertIsNone(award_candidate(1, 1, 1, cells, self.roles))

    def test_award_requires_geometry_backed_khasra_role(self):
        self.assertIsNone(award_candidate(1, 1, 1, {0: cell("22//2")}, self.roles))

    def test_neighbouring_cell_value_is_not_used_as_khasra(self):
        cells = dict(self.cells); cells[1] = cell("", 20); cells[2] = cell("22//2", 40)
        self.assertIsNone(award_candidate(1, 1, 1, cells, self.roles))

    def test_page_number_is_not_khasra(self):
        self.assertEqual(strict_khasra("8"), (None, None))

    def test_recorded_and_awarded_areas_remain_distinct(self):
        candidate = award_candidate(1, 1, 1, self.cells, self.roles)
        self.assertEqual(candidate["structuredPayload"]["recordedArea"]["normalizedSuggestion"], "22-3")
        self.assertEqual(candidate["structuredPayload"]["awardedArea"]["normalizedSuggestion"], "20-1")

    def test_raw_and_normalized_area_are_preserved(self):
        value = field("22 -- 3", cell("x")["region"], area=True)
        self.assertEqual(value["rawOcr"], "22 -- 3")
        self.assertEqual(value["normalizedSuggestion"], "22-3")

    def test_khasra_qualifier_is_preserved(self):
        candidate = award_candidate(1, 1, 1, self.cells, self.roles)
        self.assertEqual(candidate["structuredPayload"]["qualifier"], "min")

    def test_source_cell_region_survives(self):
        candidate = award_candidate(1, 1, 1, self.cells, self.roles)
        self.assertEqual(candidate["sourceRegion"], self.cells[1]["region"])

    def test_court_case_does_not_imply_stay(self):
        candidate = court_candidate(1, 1, 1, {0: cell("CWP No. 4721/2002"), 1: cell("22//2")}, {"caseNumber": 0, "khasra": 1})
        self.assertIsNone(candidate["structuredPayload"]["stay"])

    def test_weak_claim_table_is_not_structured(self):
        self.assertEqual(table_kind({0: "Name of claimant", 1: "Khasra No", 2: "Claim"})[0], "WeakClaimTable")

    def test_malformed_headers_fail_safely(self):
        self.assertIsNone(table_kind({0: "random", 1: "numbers"})[0])


if __name__ == "__main__":
    unittest.main()
