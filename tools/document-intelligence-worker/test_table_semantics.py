import sys
import unittest
from pathlib import Path
from collections import defaultdict

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "document-intelligence-benchmark"))
from table_semantics import classify_table_region, classify_inline_page, UNKNOWN
from worker import structured_from_geometry
from benchmark.normalized import BoundingBox, Word

BOX = {"x": 10, "y": 100, "width": 300, "height": 120}


def section(semantic, heading):
    return {"semantic": semantic, "rawHeading": heading, "pageStart": 1, "pageEnd": 1,
            "evidence": [{"page": 1, "rawText": heading, "sourceRegion":
                          {"x": 10, "y": 50, "width": 250, "height": 18}}]}


def cell(text):
    return {"text": text, "region": {"x": 20, "y": 140, "width": 60, "height": 18}}


def classify(semantic, heading, headers=None, data=None):
    headers = headers or {0: "Khasra", 1: "Area"}
    data = data or {0: "7//1", 1: "4-16"}
    rows = {0: {i: cell(value) for i, value in headers.items()},
            1: {i: cell(value) for i, value in data.items()}}
    return classify_table_region(1, 1, BOX, headers, rows, [section(semantic, heading)], 0)


class TableSemanticsTests(unittest.TestCase):
    def test_identical_layout_depends_on_source_heading(self):
        awarded, award_occ = classify("LAND_SCHEDULE", "Land Awarded")
        possession, poss_occ = classify("POSSESSION_REFERENCE_OR_SECTION", "Land Awarded And Taken Over")
        notified, _ = classify("STATUTORY_NOTIFICATIONS", "Delhi Administration NOTIFICATION — SPECIFICATION",
                                {0: "Field Nos. or Boundaries", 1: "Area"}, {0: "11/1", 1: "4-16"})
        self.assertEqual("SIMPLE_TWO_COLUMN", awarded["layout"])
        self.assertEqual(awarded["layout"], possession["layout"])
        self.assertEqual("AWARDED_LAND", awarded["semantic"])
        self.assertEqual("POSSESSION_LAND", possession["semantic"])
        self.assertEqual("NOTIFIED_LAND", notified["semantic"])
        self.assertEqual("7//1", award_occ[0]["normalizedKhasraNumber"])
        self.assertEqual("POSSESSION_LAND", poss_occ[0]["semantic"])
        self.assertEqual(BOX, possession["sourceRegion"])
        self.assertEqual("Land Awarded And Taken Over", possession["evidence"][0]["rawText"])
        self.assertEqual("table-source-rules/1.0", possession["classifierVersion"])
        self.assertTrue(possession["requiresHumanReview"])

    def test_comparison_claim_owner_and_court_meanings(self):
        comparison, rows = classify("LAND_SCHEDULE", "TRUE AND CORRECT AREA",
            {0: "Khasra no. of Notification", 1: "Area as per Notification", 2: "Khasra no. of Field book",
             3: "Area as per Field Book"}, {0: "61//4", 1: "4-8", 2: "61//4", 3: "1-8"})
        self.assertEqual("TRUE_CORRECT_AREA", comparison["semantic"])
        self.assertEqual(["NOTIFIED", "CORRECTED"], [x["areaFields"][0]["role"] for x in rows])
        claim, _ = classify("CLAIMS", "The following claims were filed in response to notices u/s 9 & 10",
                            {0: "Name of claimant", 1: "Khasra No", 2: "Area", 3: "Claim"},
                            {0: "Rabindra Nath", 1: "18//11", 2: "463 sq yrds", 3: "Claim"})
        self.assertEqual("CLAIM_LINKED_LAND", claim["semantic"])
        owner, _ = classify("OTHER", "A statement showing the details of Khasra Nos., ownership, area and classification of soil",
                            {0: "Name of owner", 1: "Name of occupant", 2: "Khasra No.", 3: "Area and Nature of soil"},
                            {0: "Surajmal", 1: "Self", 2: "90//11/1", 3: "4-16"})
        self.assertEqual("OWNER_LINKED_LAND", owner["semantic"])
        court_headers = {0: "CWP NO", 1: "KHASRA NO", 2: "TOTAL AREA", 3: "STATUS"}
        court, _ = classify("COURT_OR_DISPUTE_REFERENCE",
                            "The following CWPs were received in respect of Khasra No. mentioned against each",
                            court_headers, {0: "4721/2002", 1: "12//11", 2: "23-14", 3: "Status quo dispossession"})
        self.assertEqual("STAY_AFFECTED_LAND", court["semantic"])
        no_effect, _ = classify("COURT_OR_DISPUTE_REFERENCE",
                                "The following CWPs were received in respect of Khasra No. mentioned against each",
                                court_headers, {0: "4721/2002", 1: "12//11", 2: "23-14", 3: "Pending"})
        self.assertEqual(UNKNOWN, no_effect["semantic"])
        court_rows = {
            0: {i: cell(value) for i, value in court_headers.items()},
            1: {i: cell(value) for i, value in {0: "4721/2002", 1: "12//11", 2: "23-14", 3: "Status quo dispossession"}.items()},
            2: {i: cell(value) for i, value in {0: "2909/2002", 1: "11//5", 2: "21-13", 3: "Pending"}.items()},
        }
        court_table, court_mentions = classify_table_region(1, 2, BOX, court_headers, court_rows,
            [section("COURT_OR_DISPUTE_REFERENCE",
                     "The following CWPs were received in respect of Khasra No. mentioned against each")], 0)
        self.assertEqual("STAY_AFFECTED_LAND", court_table["semantic"])
        self.assertEqual(["STAY_AFFECTED_LAND", UNKNOWN], [m["semantic"] for m in court_mentions])

    def test_unknown_conflict_and_unresolved_identifier(self):
        unknown, occurrences = classify("UNKNOWN", "Unclear", data={0: "7?1", 1: "4-16"})
        self.assertEqual(UNKNOWN, unknown["semantic"])
        self.assertIsNone(occurrences[0]["normalizedKhasraNumber"])
        self.assertEqual("7?1", occurrences[0]["rawKhasraText"])
        conflict, _ = classify("CLAIMS", "Land Awarded")
        self.assertEqual(UNKNOWN, conflict["semantic"])

    def test_repeated_mentions_and_area_disagreement_remain_distinct(self):
        rows = {0: {0: cell("Khasra"), 1: cell("Area")},
                1: {0: cell("7//1 min"), 1: cell("4-16")},
                2: {0: cell("7//1 min"), 1: cell("3-12")}}
        _, occurrences = classify_table_region(1, 2, BOX, {0: "Khasra", 1: "Area"}, rows,
                                                [section("LAND_SCHEDULE", "Land Awarded")], 0)
        self.assertEqual(2, len(occurrences))
        self.assertNotEqual(occurrences[0]["occurrenceId"], occurrences[1]["occurrenceId"])
        self.assertEqual(["4-16", "3-12"], [o["areaFields"][0]["rawText"] for o in occurrences])
        self.assertEqual(["min", "min"], [o["qualifier"] for o in occurrences])
        _, rerun = classify_table_region(1, 2, BOX, {0: "Khasra", 1: "Area"}, rows,
                                         [section("LAND_SCHEDULE", "Land Awarded")], 0)
        self.assertEqual([o["occurrenceId"] for o in occurrences], [o["occurrenceId"] for o in rerun])

    def test_unknown_section_blocks_meaning(self):
        table, _ = classify("UNKNOWN", "Land Awarded")
        self.assertEqual(UNKNOWN, table["semantic"])

    def test_only_page_heading_must_precede_the_table(self):
        cases = [
            ("LAND_SCHEDULE", "Land Awarded", "AWARDED_LAND", {0: "Khasra", 1: "Area"}),
            ("CLAIMS", "The following claims were filed in response to notices u/s 9 & 10",
             "CLAIM_LINKED_LAND", {0: "Name of claimant", 1: "Khasra No", 2: "Area"}),
            ("POSSESSION_REFERENCE_OR_SECTION", "Land Awarded And Taken Over",
             "POSSESSION_LAND", {0: "Khasra", 1: "Area"}),
        ]
        for section_type, heading, expected, headers in cases:
            with self.subTest(heading=heading):
                rows = {0: {col: cell(label) for col, label in headers.items()},
                        1: {col: cell("7//1" if "khasra" in label.casefold() else "4-16")
                            for col, label in headers.items()}}
                source = section(section_type, heading)
                before, _ = classify_table_region(1, 1, BOX, headers, rows, [source], 0)
                self.assertEqual(expected, before["semantic"])
                source["evidence"][0]["sourceRegion"]["y"] = BOX["y"] + BOX["height"] + 10
                after, _ = classify_table_region(1, 1, BOX, headers, rows, [source], 0)
                self.assertEqual(UNKNOWN, after["semantic"])
                self.assertIsNone(after["sectionObservationIndex"])

    def test_continuation_needs_explicit_source_cue(self):
        previous = section("LAND_SCHEDULE", "Land Awarded")
        previous["pageEnd"] = 2
        previous["evidence"].append({"page": 2, "rawText": "Land Awarded (continued)",
                                     "sourceRegion": {"x": 10, "y": 50, "width": 250, "height": 18}})
        rows = {0: {0: cell("Khasra"), 1: cell("Area")},
                1: {0: cell("7//2"), 1: cell("4-16")}}
        table, mentions = classify_table_region(2, 1, BOX, {0: "Khasra", 1: "Area"}, rows, [previous], 0)
        self.assertEqual("AWARDED_LAND", table["semantic"])
        self.assertEqual(2, len([e for e in table["evidence"] if "Land Awarded" in e["rawText"]]))
        self.assertEqual("7//2", mentions[0]["rawKhasraText"])
        previous["evidence"].pop()
        unknown, _ = classify_table_region(2, 1, BOX, {0: "Khasra", 1: "Area"}, rows, [previous], 0)
        self.assertEqual(UNKNOWN, unknown["semantic"])
        previous["evidence"].append({"page": 2, "rawText": "Land Awarded (continued)",
                                     "sourceRegion": {"x": 10, "y": 240, "width": 250, "height": 18}})
        later_cue, _ = classify_table_region(2, 1, BOX, {0: "Khasra", 1: "Area"}, rows, [previous], 0)
        self.assertEqual(UNKNOWN, later_cue["semantic"])

    def test_existing_geometry_join_can_stage_observation_without_legacy_award_candidate(self):
        geometry = [{"label": "table", "box": {"x": 0, "y": 40, "width": 200, "height": 100}},
                    {"label": "table row", "box": {"x": 0, "y": 40, "width": 200, "height": 45}},
                    {"label": "table row", "box": {"x": 0, "y": 85, "width": 200, "height": 55}},
                    {"label": "table column", "box": {"x": 0, "y": 40, "width": 100, "height": 100}},
                    {"label": "table column", "box": {"x": 100, "y": 40, "width": 100, "height": 100}}]
        words = [Word(text, BoundingBox(x, y, 40, 15), .99) for text, x, y in
                 [("Khasra", 10, 55), ("Area", 110, 55), ("7//1", 10, 100), ("4-16", 110, 100)]]
        observations, mentions = [], []
        source_section = section("LAND_SCHEDULE", "Land Awarded")
        source_section["evidence"][0]["sourceRegion"]["y"] = 10
        candidates, _ = structured_from_geometry(1, geometry, words, None, None, {}, defaultdict(int),
            semantic_words=words, semantic_sections=[source_section],
            table_observations=observations, khasra_occurrences=mentions)
        self.assertEqual([], candidates)
        self.assertEqual("AWARDED_LAND", observations[0]["semantic"])
        self.assertEqual("7//1", mentions[0]["rawKhasraText"])

    def test_explicit_inline_specification_keeps_noncanonical_raw_identifiers(self):
        lines = [{"text": "Delhi Administration NOTIFICATION — SPECIFICATION", "sourceRegion":
                  {"x": 10, "y": 50, "width": 250, "height": 18}},
                 {"text": "Field Nos. or Boundaries: 11/1, 11/2", "sourceRegion":
                  {"x": 10, "y": 100, "width": 300, "height": 18}}]
        table, mentions = classify_inline_page(1, lines,
            [section("STATUTORY_NOTIFICATIONS", lines[0]["text"])])
        self.assertEqual("NOTIFIED_LAND", table["semantic"])
        self.assertEqual("INLINE_PARAGRAPH_LIST", table["layout"])
        self.assertEqual(["11/1", "11/2"], [x["rawKhasraText"] for x in mentions])
        self.assertTrue(all(x["normalizedKhasraNumber"] is None for x in mentions))
        unknown, raw_mentions = classify_inline_page(1, lines, [section("UNKNOWN", "Unclear")])
        self.assertEqual(UNKNOWN, unknown["semantic"])
        self.assertEqual(2, len(raw_mentions))


if __name__ == "__main__":
    unittest.main()
