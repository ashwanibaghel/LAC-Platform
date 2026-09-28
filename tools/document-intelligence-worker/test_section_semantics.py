import unittest

from section_semantics import classify_sections


def page(number, *texts):
    return {"page": number, "lines": [{"text": text, "sourceRegion":
        {"x": 10, "y": 10 + i * 20, "width": 240, "height": 18}}
        for i, text in enumerate(texts)]}


class SectionSemanticsTests(unittest.TestCase):
    def test_audited_heading_concepts_keep_source_evidence(self):
        examples = [
            ("Award No. 30/2002-03", "AWARD_IDENTITY"),
            ("Notifications", "STATUTORY_NOTIFICATIONS"),
            ("The following claims were filed in response to notices u/s 9 & 10", "CLAIMS"),
            ("Market Value", "VALUATION"),
            ("Compensation Calculation", "COMPENSATION_CALCULATION"),
            ("Land Awarded", "LAND_SCHEDULE"),
            ("Annexure B Land Awarded And Taken Over", "POSSESSION_REFERENCE_OR_SECTION"),
            ("The following CWPs were received in respect of Khasra No. mentioned against each", "COURT_OR_DISPUTE_REFERENCE"),
            ("Apportionment", "APPORTIONMENT_OR_ENTITLEMENT"),
            ("Supplementary Award", "SUPPLEMENTARY_MATTER"),
            ("Delhi Administration NOTIFICATION — SPECIFICATION", "STATUTORY_NOTIFICATIONS"),
            ("A statement showing the details of Khasra Nos., ownership, area and classification of soil", "OTHER"),
        ]
        for heading, expected in examples:
            with self.subTest(heading=heading):
                item = classify_sections([page(1, heading)])[0]
                self.assertEqual(expected, item["semantic"])
                self.assertEqual(heading, item["evidence"][0]["rawText"])
                self.assertTrue(item["requiresHumanReview"])

    def test_multiple_repeated_and_restarted_sections_remain_separate(self):
        rows = classify_sections([page(1, "Claims", "Market Value"), page(2, "Claims")])
        self.assertEqual(["CLAIMS", "VALUATION", "CLAIMS"], [r["semantic"] for r in rows])
        self.assertEqual([1, 1, 2], [r["pageStart"] for r in rows])

    def test_explicit_continuation_extends_but_unproven_page_is_unknown(self):
        rows = classify_sections([page(1, "Land Awarded"), page(2, "Land Awarded (continued)"),
                                  page(3, "some unrelated body text"), page(4, "continued")])
        self.assertEqual((1, 2), (rows[0]["pageStart"], rows[0]["pageEnd"]))
        self.assertEqual(["LAND_SCHEDULE", "UNKNOWN", "UNKNOWN"], [r["semantic"] for r in rows])

    def test_unknown_conflict_body_mention_and_unreadable_page(self):
        rows = classify_sections([page(1, "Claims and compensation"),
                                  page(2, "The market value is discussed here in the following paragraph."),
                                  page(3)])
        self.assertEqual(["UNKNOWN"] * 3, [r["semantic"] for r in rows])
        self.assertEqual([], rows[2]["evidence"])

    def test_form_does_not_assert_business_meaning(self):
        item = classify_sections([page(1, "Land Schedule", "S.NO KHASRA AREA")])[0]
        self.assertEqual("SCHEDULE", item["presentation"])
        self.assertEqual("LAND_SCHEDULE", item["semantic"])
        self.assertNotIn("khasra", item)

    def test_context_never_promotes_possession_stay_or_payee_facts(self):
        rows = classify_sections([page(1, "Land Awarded", "The following CWPs were received in respect of Khasra No. mentioned against each",
                                      "The following claims were filed in response to notices u/s 9 & 10")])
        self.assertEqual(["LAND_SCHEDULE", "COURT_OR_DISPUTE_REFERENCE", "CLAIMS"],
                         [row["semantic"] for row in rows])
        for row in rows:
            self.assertTrue({"observationType", "semantic", "presentation", "pageStart", "pageEnd",
                             "rawHeading", "confidence", "requiresHumanReview", "evidence", "warnings",
                             "classifierVersion"} == set(row))


if __name__ == "__main__":
    unittest.main()
