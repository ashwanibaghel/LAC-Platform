import sys
import json
from collections import defaultdict
from unittest.mock import patch
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(ROOT.parent / "document-intelligence-worker"))

from benchmark.normalized import BoundingBox, Word
from primary_land_schedule import (
    CLAIMANT, CLASSIFICATION, COURT, POSSESSION, PRIMARY, UNKNOWN,
    classify_land_section, primary_land_candidates, primary_land_source_rows, section_for_table,
)
import worker


def word(value, x, y, width=65, height=22, confidence=.999):
    return Word(value, BoundingBox(x, y, width, height), confidence)


THREE_HEADERS = "RecNo Khasra Total Area Area Awarded RecNoKhasra Total Area Area Awarded RecNo Khasra Total Area Area Awarded"
TWO_HEADERS = "RecNo Khasra Total Area Awarded Area RecNo Khasra Total Area Awarded Area"


def primary_words():
    return [word("Land Awarded", 90, 95, 300, 35), word(THREE_HEADERS, 90, 180, 1040, 28)]


class PrimaryLandScheduleTests(unittest.TestCase):
    def test_real_pdf_manifest_has_page_group_counts_and_review_boundary(self):
        manifest = json.loads((ROOT / "fixtures" / "pochanpur_primary_land_manifest.json").read_text(encoding="utf-8"))
        regions = json.loads((ROOT / "fixtures" / "pochanpur_primary_row_regions.json").read_text(encoding="utf-8"))
        self.assertEqual(707, sum(sum(groups) for groups in manifest["physicalRowsByPageAndGroup"].values()))
        self.assertEqual({page: [len(group) for group in groups] for page, groups in regions["rowCentersByPageAndGroup"].items()}, manifest["physicalRowsByPageAndGroup"])
        self.assertEqual(707, manifest["physicalRowsTotal"])
        self.assertGreaterEqual(len(manifest["verifiedSamples"]), 40)
        self.assertEqual("REVIEW", manifest["untranscribedRowStatus"])
        self.assertEqual([2, 3, 4, 5, 6], manifest["sourcePages"])

    def test_source_heading_and_three_repeated_header_schemas(self):
        layout = classify_land_section(primary_words(), 1220, 2012)
        self.assertEqual(PRIMARY, layout.section)
        self.assertEqual(3, layout.groups)
        self.assertFalse(layout.inherited)

    def test_actual_awarded_area_alias_and_continuation(self):
        first = classify_land_section(primary_words(), 1220, 2012)
        continuation = [word(THREE_HEADERS, 94, 170, 1040, 28), word("1//25", 130, 225)]
        second = classify_land_section(continuation, 1220, 2012, first)
        self.assertEqual(PRIMARY, second.section)
        self.assertTrue(second.inherited)
        self.assertIsNone(classify_land_section([word(THREE_HEADERS, 220, 170, 900, 28)], 1220, 2012, first))

    def test_continuation_stops_on_section_change_or_missing_header(self):
        first = classify_land_section(primary_words(), 1220, 2012)
        possession = [word("Annexure B Land Awarded And Taken Over", 90, 95, 800, 35), word(TWO_HEADERS, 90, 180, 1040, 28)]
        changed = classify_land_section(possession, 1220, 2012, first)
        self.assertEqual(POSSESSION, changed.section)
        self.assertEqual(2, changed.groups)
        self.assertEqual([], primary_land_candidates(18, possession, 2012, changed))
        self.assertIsNone(classify_land_section([word("Court CWP No Khasra Status", 90, 180, 700, 28)], 1220, 2012, first))
        self.assertIsNone(classify_land_section([word("random prose", 90, 180)], 1220, 2012, first))

    def test_distinct_table_semantics(self):
        primary = classify_land_section(primary_words(), 1220, 2012)
        possession = classify_land_section([word("Land Awarded And Taken Over", 90, 95, 500, 35), word(TWO_HEADERS, 90, 180, 1040, 28)], 1220, 2012)
        self.assertEqual(PRIMARY, section_for_table("AwardLandTable", primary))
        self.assertEqual(POSSESSION, section_for_table("AwardLandTable", possession))
        self.assertEqual(UNKNOWN, section_for_table("AwardLandTable", None))
        self.assertEqual(COURT, section_for_table("CourtCwpTable", primary))
        self.assertEqual(CLAIMANT, section_for_table("WeakClaimTable", primary))
        self.assertEqual(CLASSIFICATION, section_for_table("LandClassification", primary))

    def test_three_groups_one_physical_row_without_cross_column_borrowing(self):
        words = primary_words() + [
            word("1//25", 130, 330), word("0 -- 14", 235, 332), word("0 -- 14", 355, 335),
            word("7//15", 470, 333), word("4 -- 9", 575, 335), word("4 -- 9", 695, 336),
            word("9//5/2min", 810, 334, 95), word("1 -- 0", 915, 337), word("1 -- 0", 1035, 337),
        ]
        layout = classify_land_section(words, 1220, 2012)
        candidates = primary_land_candidates(2, words, 2012, layout)
        self.assertEqual(3, len(candidates))
        by_group = {c["structuredPayload"]["logicalGroupId"]: c for c in candidates}
        self.assertEqual("0-14", by_group[1]["structuredPayload"]["recordedArea"]["normalizedSuggestion"])
        self.assertEqual("4-9", by_group[2]["structuredPayload"]["awardedArea"]["normalizedSuggestion"])
        self.assertEqual("min", by_group[3]["structuredPayload"]["qualifier"])
        self.assertTrue(all(c["requiresIndividualReview"] for c in candidates))
        self.assertEqual(PRIMARY, by_group[1]["structuredPayload"]["sourceSection"])
        self.assertEqual(2, by_group[1]["page"])
        self.assertEqual(1, by_group[1]["structuredPayload"]["sourceCells"]["khasra"]["logicalGroupId"])

    def test_blank_malformed_area_never_copies_other_column_or_row(self):
        words = primary_words() + [
            word("11//22/3min", 130, 330, 90), word("0 -- 3", 235, 332),
            word("11//22/3min", 130, 364, 90), word("0 -- 12", 235, 366), word("garbled", 355, 367),
        ]
        layout = classify_land_section(words, 1220, 2012)
        candidates = primary_land_candidates(3, words, 2012, layout)
        self.assertEqual(2, len(candidates))
        self.assertIsNone(candidates[0]["structuredPayload"]["awardedArea"]["normalizedSuggestion"])
        self.assertIsNone(candidates[1]["structuredPayload"]["awardedArea"]["normalizedSuggestion"])
        self.assertEqual("0-3", candidates[0]["structuredPayload"]["recordedArea"]["normalizedSuggestion"])
        self.assertEqual("0-12", candidates[1]["structuredPayload"]["recordedArea"]["normalizedSuggestion"])

    def test_strict_identifier_and_unusual_subdivision(self):
        words = primary_words() + [
            word("11//22/3/1", 130, 330, 90), word("0-3", 235, 332), word("0-3", 355, 334),
            word("11//22/3?", 130, 365, 90), word("0-12", 235, 366), word("0-12", 355, 368),
        ]
        layout = classify_land_section(words, 1220, 2012)
        candidates = primary_land_candidates(3, words, 2012, layout)
        self.assertEqual(["11//22/3/1"], [c["normalizedSuggestion"] for c in candidates])
        self.assertTrue(candidates[0]["requiresIndividualReview"])
        unresolved = primary_land_source_rows(3, words, 2012, layout, candidates)
        self.assertEqual(1, len(unresolved))
        self.assertEqual("REVIEW", unresolved[0]["identifierStatus"])
        self.assertEqual("11//22/3?", unresolved[0]["sourceCells"]["khasra"][0]["rawOcr"])
        self.assertEqual("0-12", unresolved[0]["sourceCells"]["recordedArea"][0]["rawOcr"])

    def test_possession_grid_retained_as_evidence_and_not_award_khasra(self):
        layout = classify_land_section([word("Land Awarded And Taken Over", 90, 95, 500, 35),
                                        word(TWO_HEADERS, 90, 180, 1040, 28)], 1220, 2012)
        headers = {0: "RecNo", 1: "Khasra", 2: "Total Area", 3: "Awarded Area"}
        rows = {0: {column: {"text": value, "region": {"x": column * 20, "y": 10, "width": 20, "height": 10}}
                    for column, value in headers.items()},
                1: {1: {"text": "1//25", "region": {"x": 20, "y": 30, "width": 20, "height": 10}},
                    2: {"text": "0-14", "region": {"x": 40, "y": 30, "width": 20, "height": 10}},
                    3: {"text": "0-14", "region": {"x": 60, "y": 30, "width": 20, "height": 10}}}}
        evidence = []
        with patch.object(worker, "rows_for_table", return_value=(rows, 4)), patch.object(worker, "header_cells_for_table", return_value=headers):
            candidates, counts = worker.structured_from_geometry(
                18, [{"label": "table", "box": {"x": 0, "y": 0, "width": 100, "height": 100}}],
                [], None, None, {}, defaultdict(int), land_layout=layout, possession_evidence=evidence)
        self.assertEqual([], candidates)
        self.assertEqual(0, counts["awardRows"])
        self.assertEqual(1, len(evidence))
        self.assertEqual(POSSESSION, evidence[0]["sourceSection"])
        self.assertEqual("1//25", evidence[0]["sourceCells"]["khasra"]["rawOcr"])


if __name__ == "__main__":
    unittest.main()
