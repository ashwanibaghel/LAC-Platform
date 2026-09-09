import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from benchmark.worker_semantics import award_candidate, award_table_groups, court_candidate, field, infer_court_table_kind, strict_khasra, table_kind


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

    def test_cell_and_page_ocr_agreement_is_preserved(self):
        value = field("22//2", cell("x")["region"], cell_crop_ocr="22//2")
        self.assertEqual(value["pageAssignedOcr"], "22//2")
        self.assertEqual(value["cellCropOcr"], "22//2")
        self.assertEqual(value["recognitionWarnings"], [])

    def test_cell_and_page_ocr_disagreement_is_preserved(self):
        value = field("22//2", cell("x")["region"], cell_crop_ocr="22//9")
        self.assertEqual(value["pageAssignedOcr"], "22//2")
        self.assertEqual(value["cellCropOcr"], "22//9")
        self.assertTrue(value["recognitionWarnings"])

    def test_khasra_qualifier_is_preserved(self):
        candidate = award_candidate(1, 1, 1, self.cells, self.roles)
        self.assertEqual(candidate["structuredPayload"]["qualifier"], "min")

    def test_source_cell_region_survives(self):
        candidate = award_candidate(1, 1, 1, self.cells, self.roles)
        self.assertEqual(candidate["sourceRegion"], self.cells[1]["region"])

    def test_court_case_does_not_imply_stay(self):
        candidate = court_candidate(1, 1, 1, {0: cell("4721/2002"), 1: cell("22//2"), 2: cell("23-14"), 3: cell("Status quo")}, {"caseNumber": 0, "khasra": 1, "area": 2, "status": 3, "headerLabels": ["CWP No", "Khasra No", "Total Area", "Status"]})
        payload = candidate["structuredPayload"]
        self.assertEqual("CWP", payload["caseType"])
        self.assertEqual("4721/2002", payload["caseNumber"]["normalizedSuggestion"])
        self.assertEqual("22//2", payload["khasraReferences"]["rawOcr"])
        self.assertEqual("23-14", payload["relatedAreaText"]["normalizedSuggestion"])
        self.assertEqual("Status quo", payload["status"]["rawOcr"])
        self.assertNotIn("stay", payload)
        self.assertEqual(1, payload["sourceCells"]["status"]["rowId"])

    def test_court_table_requires_complete_case_number_and_never_crosses_rows(self):
        roles = {"caseNumber": 0, "khasra": 1, "area": 2, "status": 3, "headerLabels": ["CWP No", "Khasra No", "Total Area", "Status"]}
        self.assertIsNone(court_candidate(1, 1, 1, {0: cell("47?1/2002"), 1: cell("22//2"), 2: cell("23-14"), 3: cell("Status quo")}, roles))
        row_one = court_candidate(1, 1, 1, {0: cell("4721/2002"), 1: cell("22//2"), 2: cell("23-14"), 3: cell("Pending")}, roles)
        row_two = court_candidate(1, 1, 2, {0: cell("2909/2002"), 1: cell("11//5"), 2: cell("21-13"), 3: cell("Disposed")}, roles)
        self.assertEqual("22//2", row_one["structuredPayload"]["khasraReferences"]["rawOcr"])
        self.assertEqual("11//5", row_two["structuredPayload"]["khasraReferences"]["rawOcr"])
        self.assertEqual(1, row_one["structuredPayload"]["sourceCells"]["khasraReferences"]["rowId"])
        self.assertEqual(2, row_two["structuredPayload"]["sourceCells"]["khasraReferences"]["rowId"])

    def test_missing_cwp_header_requires_same_grid_status_khasra_and_complete_case_column(self):
        headers = {1: "Khasra No", 3: "Status"}
        rows = {0: {1: cell("Khasra No"), 3: cell("Status")},
                1: {0: cell("4721/2002"), 1: cell("12//11"), 3: cell("Status quo")}}
        kind, roles = infer_court_table_kind(headers, rows, 0)
        self.assertEqual("CourtCwpTable", kind)
        self.assertEqual(0, roles["caseNumber"])
        self.assertEqual(1, roles["khasra"])

    def test_missing_cwp_header_never_accepts_damaged_case_identifier(self):
        headers = {1: "Khasra No", 3: "Status"}
        rows = {0: {1: cell("Khasra No"), 3: cell("Status")},
                1: {0: cell("47?1/2002"), 1: cell("12//11"), 3: cell("Status quo")}}
        self.assertIsNone(infer_court_table_kind(headers, rows, 0)[0])

    def test_weak_claim_table_is_not_structured(self):
        self.assertEqual(table_kind({0: "Name of claimant", 1: "Khasra No", 2: "Claim"})[0], "WeakClaimTable")

    def test_malformed_headers_fail_safely(self):
        self.assertIsNone(table_kind({0: "random", 1: "numbers"})[0])

    def test_two_up_award_schema_keeps_physical_columns_separate(self):
        headers = {
            0: "Rec No", 1: "Khasra", 2: "Total Area", 3: "Area Awarded",
            4: "Rec No", 5: "Khasra", 6: "Total Area", 7: "Area Awarded",
        }
        groups = award_table_groups(headers)
        self.assertEqual(
            [{"logicalGroupId": 1, "khasra": 1, "recordedArea": 2, "awardedArea": 3, "rectangle": 0},
             {"logicalGroupId": 2, "khasra": 5, "recordedArea": 6, "awardedArea": 7, "rectangle": 4}],
            groups,
        )
        row = {
            0: cell("1", 0), 1: cell("1//25", 20), 2: cell("0-14", 40), 3: cell("0-14", 60),
            4: cell("7", 80), 5: cell("7//15", 100), 6: cell("4-9", 120), 7: cell("4-9", 140),
        }
        left, right = [award_candidate(18, 1, 6, row, group) for group in groups]
        self.assertEqual("0-14", left["structuredPayload"]["recordedArea"]["normalizedSuggestion"])
        self.assertEqual("0-14", left["structuredPayload"]["awardedArea"]["normalizedSuggestion"])
        self.assertEqual("4-9", right["structuredPayload"]["recordedArea"]["normalizedSuggestion"])
        self.assertEqual("4-9", right["structuredPayload"]["awardedArea"]["normalizedSuggestion"])
        self.assertEqual(1, left["structuredPayload"]["sourceCells"]["awardedArea"]["logicalGroupId"])
        self.assertEqual(2, right["structuredPayload"]["sourceCells"]["awardedArea"]["logicalGroupId"])

    def test_two_up_uneven_rows_never_borrow_missing_area(self):
        headers = {0: "Khasra", 1: "Total Area", 2: "Area Awarded", 3: "Khasra", 4: "Total Area", 5: "Area Awarded"}
        left_group, right_group = award_table_groups(headers)
        row = {0: cell("1//25", 0), 1: cell("", 20), 2: cell("", 40), 3: cell("7//15", 60), 4: cell("4-9", 80), 5: cell("4-9", 100)}
        left = award_candidate(18, 1, 7, row, left_group)
        right = award_candidate(18, 1, 7, row, right_group)
        self.assertIsNone(left["structuredPayload"]["recordedArea"]["normalizedSuggestion"])
        self.assertIsNone(left["structuredPayload"]["awardedArea"]["normalizedSuggestion"])
        self.assertEqual("4-9", right["structuredPayload"]["awardedArea"]["normalizedSuggestion"])

    def test_missed_left_awarded_header_cannot_bind_the_right_schema(self):
        # A detector/OCR miss in the left header must suppress that group,
        # rather than letting its Khasra consume the right-side Awarded column.
        headers = {0: "Rec No", 1: "Khasra", 2: "Total Area", 4: "Rec No", 5: "Khasra", 6: "Total Area", 7: "Area Awarded"}
        self.assertEqual(
            [{"logicalGroupId": 2, "khasra": 5, "recordedArea": 6, "awardedArea": 7, "rectangle": 4}],
            award_table_groups(headers),
        )

    def test_complete_grid_recovers_missed_peer_header_by_stride_not_values(self):
        # The right header is recognised but the left heading is faint. The
        # physical 8-column grid still proves two 4-column schemas.
        headers = {5: "Khasra", 6: "Total Area", 7: "Area Awarded"}
        self.assertEqual(
            [{"logicalGroupId": 1, "khasra": 1, "recordedArea": 2, "awardedArea": 3, "rectangle": 0},
             {"logicalGroupId": 2, "khasra": 5, "recordedArea": 6, "awardedArea": 7, "rectangle": 4}],
            award_table_groups(headers, column_count=8),
        )


if __name__ == "__main__":
    unittest.main()
