import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "document-intelligence-worker"))
from worker import narrative_core_and_statutory_candidates, valuation_and_compensation_candidates


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


if __name__ == "__main__":
    unittest.main()
