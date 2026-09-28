import sys
import unittest
from pathlib import Path

WORKER = Path(__file__).resolve().parents[2] / "document-intelligence-worker"
sys.path.insert(0, str(WORKER))
from document_genre import classify_document, should_extract_award


def page(number, *lines):
    return {"page": number, "lines": [
        {"text": text, "sourceRegion": {"x": 10, "y": index * 24, "width": 240, "height": 18}}
        for index, text in enumerate(lines)
    ]}


class DocumentGenreTests(unittest.TestCase):
    def test_source_award_heading_and_statutory_context(self):
        decision = classify_document([page(1, "AWARD NO. 30/2002-03", "under the Land Acquisition Act")])
        self.assertEqual("AWARD", decision["genre"])
        self.assertTrue(should_extract_award(decision["genre"]))
        self.assertEqual([1], sorted({item["page"] for item in decision["evidence"]}))

    def test_supplementary_award_is_distinct(self):
        decision = classify_document([page(1, "SUPPLEMENTARY AWARD", "AWARD NO. 30/2002-03", "under the Land Acquisition Act")])
        self.assertEqual("SUPPLEMENTARY_AWARD", decision["genre"])
        self.assertTrue(should_extract_award(decision["genre"]))

    def test_audited_kabza_karwayi_title_blocks_award_route(self):
        decision = classify_document([page(1, "KABZA KARWAYI", "Award No. 15 is referred to below",
                                           "under the Land Acquisition Act")])
        self.assertEqual("POSSESSION_PROCEEDINGS", decision["genre"])
        self.assertFalse(should_extract_award(decision["genre"]))

    def test_audited_annual_rental_offer_blocks_award_route(self):
        decision = classify_document([page(1, "Annual rental offer for requisitioned land")])
        self.assertEqual("RENTAL_OR_REQUISITION_OFFER", decision["genre"])
        self.assertFalse(should_extract_award(decision["genre"]))

    def test_claimant_register_continuation_is_distinct(self):
        decision = classify_document([page(1, "CLAIMANT REGISTER CONTINUATION")])
        self.assertEqual("CLAIMANT_REGISTER_OR_CONTINUATION", decision["genre"])
        self.assertFalse(should_extract_award(decision["genre"]))

    def test_weak_or_unreadable_source_is_unknown(self):
        for text in ("Compensation details follow", "Award mentioned in the case", ""):
            decision = classify_document([page(1, text)])
            self.assertEqual("UNKNOWN", decision["genre"])
            self.assertTrue(decision["requiresHumanReview"])
            self.assertFalse(should_extract_award(decision["genre"]))

    def test_competing_titles_on_different_pages_remain_unknown(self):
        decision = classify_document([
            page(1, "AWARD NO. 30/2002-03", "under the Land Acquisition Act"),
            page(2, "POSSESSION PROCEEDINGS"),
        ])
        self.assertEqual("UNKNOWN", decision["genre"])
        self.assertEqual([1, 2], sorted({item["page"] for item in decision["evidence"]}))
        self.assertFalse(should_extract_award(decision["genre"]))

    def test_competing_evidence_retains_both_genres_when_one_repeats(self):
        decision = classify_document([
            page(1, *("Annual rental offer for requisitioned land" for _ in range(12))),
            page(2, "KABZA KARWAYI"),
        ])
        self.assertEqual("UNKNOWN", decision["genre"])
        self.assertEqual([1, 2], sorted({item["page"] for item in decision["evidence"]}))

    def test_filename_and_award_link_never_supply_source_evidence(self):
        source = page(1, "Unreadable scan")
        source["filename"] = "award-30.pdf"
        source["targetAwardId"] = "11111111-1111-1111-1111-111111111111"
        self.assertEqual("UNKNOWN", classify_document([source])["genre"])


if __name__ == "__main__":
    unittest.main()
