import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "document-intelligence-worker"))
from worker import narrative_core_and_statutory_candidates, valuation_and_compensation_candidates, possession_candidates, court_case_candidates, _nm_band, nm_pilot_candidates, nm_semantic_candidates


def words(*values):
    result = []
    y = 10
    for value in values:
        x = 10
        for token in value.split():
            result.append(SimpleNamespace(text=token, bounding_box=SimpleNamespace(x=x, y=y, width=max(8, len(token) * 7), height=12)))
            x += max(8, len(token) * 7) + 4
        y += 24
    return result


class AwardCoreWorkerTests(unittest.TestCase):
    def test_nm_incomplete_fragment_never_becomes_review_row(self):
        item = _nm_band(1, 1, [("Ramesh Khasra 12//2", {"x": 1, "y": 1, "width": 80, "height": 12})])
        self.assertEqual("UnassignedSourceFragment", item["candidateType"])
        self.assertEqual("FragmentOnly", item["structuredPayload"]["groupingState"])

    def test_nm_safe_group_requires_independent_anchors(self):
        item = _nm_band(1, 1, [("1 Ramesh Kumar S/o Mohan Khasra 12//2 0-14 Rs. 200", {"x": 1, "y": 1, "width": 200, "height": 12})])
        self.assertEqual("NmReviewRow", item["candidateType"])
        self.assertEqual("SafeForReview", item["structuredPayload"]["groupingState"])

    def test_numeric_lines_cannot_open_owner_records(self):
        output = nm_pilot_candidates(1, words("12//2 0-14 Rs. 200", "3 0-10 Rs. 100"), 500, 500)
        self.assertFalse(any(item["candidateType"] == "NmReviewRow" for item in output))

    def test_one_multiline_owner_keeps_numeric_continuations_in_one_block(self):
        output = nm_pilot_candidates(1, words("1 Ramesh Kumar S/o Mohan", "Khasra 12//2 0-14 Rs. 200", "Compensation Rs. 20"), 500, 500)
        self.assertEqual(1, sum(item["candidateType"] == "NmReviewRow" for item in output))

    def test_two_owner_anchors_make_two_blocks(self):
        output = nm_pilot_candidates(1, words("1 Ramesh Kumar S/o Mohan Khasra 12//2 0-14 Rs. 200", "2 Suresh Kumar S/o Hari Khasra 13//2 0-10 Rs. 100"), 500, 500)
        self.assertEqual(2, sum(item["candidateType"] == "NmReviewRow" for item in output))
    def test_semantic_nm_missing_schema_is_exception_not_review_row(self):
        output = nm_semantic_candidates(1, words("unreadable source fragment"), 500)
        self.assertEqual("NmSemanticException", output[0]["candidateType"])
        self.assertEqual("PageSchemaMissing", output[0]["structuredPayload"]["reason"])
    def test_supplementary_parent_is_a_suggestion(self):
        output = narrative_core_and_statutory_candidates(1, words("Supplementary Award", "Award No: SUP-2/2026", "Main Award No. MAIN-1/2025"))
        core = next(item for item in output if item["candidateType"] == "AwardCore")["structuredPayload"]
        self.assertEqual("Supplementary", core["awardType"])
        self.assertEqual("MAIN-1/2025", core["parentAwardReferenceSuggestion"])

    def test_nh_and_la_sections_are_not_forced_together(self):
        nh = narrative_core_and_statutory_candidates(1, words("National Highways Act Section 3A Notification No. NH-3A/7 dated 01/01/2026"))
        item = next(value for value in nh if value["candidateType"] == "Notification")["structuredPayload"]
        self.assertEqual("National Highways Act", item["legalFramework"])
        self.assertEqual("Section 3A", item["section"])
        la = narrative_core_and_statutory_candidates(1, words("Land Acquisition Act Section 4 Notification No. LA-4/7 dated 01/01/2026"))
        self.assertEqual("Section 4", next(value for value in la if value["candidateType"] == "Notification")["structuredPayload"]["section"])

    def test_bare_legal_reference_does_not_fabricate_notification_or_digits(self):
        output = narrative_core_and_statutory_candidates(1, words("National Highways Act Section 3D is applicable."))
        self.assertFalse(any(value["candidateType"] == "Notification" for value in output))
        self.assertTrue(any(value["candidateType"] == "UnmappedAwardFinding" for value in output))

    def test_rates_rules_and_summary_remain_separate(self):
        output = valuation_and_compensation_candidates(1, words("Market value Rs. 5000 per acre", "Solatium 30% Rs. 1500", "Additional amount 12%", "Balance amount Rs. 100"))
        self.assertEqual("Market value", next(item for item in output if item["candidateType"] == "ValuationRule")["structuredPayload"]["ruleType"])
        rules = [item["structuredPayload"] for item in output if item["candidateType"] == "CompensationRule"]
        self.assertEqual({"Solatium", "AdditionalAmount"}, {item["ruleType"] for item in rules})
        self.assertTrue(any(item["structuredPayload"]["category"] == "Award summary component" for item in output if item["candidateType"] == "UnmappedAwardFinding"))

    def test_100_percent_and_structure_context_are_not_owner_or_khasra_records(self):
        output = valuation_and_compensation_candidates(1, words("Solatium 100%", "Structure valuation assessed Rs. 9000 Khasra 1//25"))
        self.assertIn("100", [item["structuredPayload"]["ratePercent"] for item in output if item["candidateType"] == "CompensationRule"])
        asset = next(item for item in output if item["candidateType"] == "SupplementaryMatter")
        self.assertNotIn("owner", asset["structuredPayload"].get("description", "").lower())
        self.assertFalse(any(item["candidateType"] == "AwardKhasra" for item in output))

    def test_possession_events_preserve_multiple_occurrences_without_parcel_links(self):
        output = possession_candidates(1, words(
            "Physical possession taken on 11.09.2002 for area 1-2 Khasra No. 12//2.",
            "Balance possession taken over on 12.09.2002 for area 0-10.",
        ))
        self.assertEqual(2, len(output))
        first, second = [item["structuredPayload"] for item in output]
        self.assertEqual("2002-09-11", first["possessionDate"])
        self.assertEqual("1-2", first["possessionAreaText"])
        self.assertEqual("12//2", first["khasraReferences"])
        self.assertEqual("2002-09-12", second["possessionDate"])
        self.assertEqual("0-10", second["possessionAreaText"])

    def test_court_stay_without_possession_never_creates_possession_event(self):
        self.assertEqual([], possession_candidates(1, words("CWP 123/2026 was stayed by the Court.")))

    def test_explicit_possession_stay_and_bad_date_remain_review_evidence(self):
        output = possession_candidates(1, words("Possession is stayed on 31.02.2002."))
        self.assertEqual(1, len(output))
        payload = output[0]["structuredPayload"]
        self.assertEqual("Possession stayed", payload["eventType"])
        self.assertIsNone(payload["possessionDate"])
        self.assertTrue(any("could not be safely normalized" in item for item in output[0]["interpretationWarnings"]))

    def test_identifiable_cwp_is_review_only_and_preserves_exact_status(self):
        output = court_case_candidates(1, words("CWP 4721/2002 status: Status quo Khasra No. 12//11 area 23-14."))
        self.assertEqual(1, len(output))
        value = output[0]["structuredPayload"]
        self.assertEqual("CWP 4721/2002", value["caseNumber"])
        self.assertEqual("CWP", value["caseType"])
        self.assertEqual("Status quo", value["status"])
        self.assertEqual("12//11", value["khasraReferences"])
        self.assertEqual("23-14", value["relatedAreaText"])
        self.assertNotIn("stay", str(value).lower())

    def test_court_procedural_clause_or_uncertain_digit_never_becomes_case(self):
        self.assertEqual([], court_case_candidates(1, words("The dispute shall be referred to Civil Court.")))
        self.assertEqual([], court_case_candidates(1, words("CWP 47?1/2002 is mentioned.")))

    def test_repeated_case_occurrences_are_retained_and_do_not_create_possession(self):
        source = words("CWP 4721/2002 pending.", "CWP 4721/2002 disposed.")
        self.assertEqual(2, len(court_case_candidates(1, source)))
        self.assertEqual([], possession_candidates(1, source))


if __name__ == "__main__":
    unittest.main()
