import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from benchmark.interpret import interpret
from benchmark.normalized import Block, BoundingBox, DocumentPage


def page(text: str) -> DocumentPage:
    return DocumentPage(1, 100, 100, [Block("Table", text, BoundingBox(0, 0, 100, 100))])


class InterpretationTests(unittest.TestCase):
    def test_cwp_multiple_khasras_are_relationship_occurrences(self):
        values = interpret([page("CWP No. 4721/2002 KHASRA NO 12//11, 12//12/2")])
        self.assertEqual([x.value for x in values if x.concept == "CourtCaseNumber"], ["CWP No. 4721/2002"])
        self.assertFalse(any(x.classification != "NeedsHumanInterpretation" for x in values))

    def test_numeric_near_match_is_not_repaired(self):
        values = interpret([page("KHASRA TOTAL AREA AREA AWARDED 22//2/7 0-14 0-14")])
        self.assertIn("22//2/7", [x.value for x in values])
        self.assertNotIn("22//2/1", [x.value for x in values])

    def test_claim_is_not_payment_or_owner(self):
        values = interpret([page("CLAIM AND EVIDENCE NAME OF CLAIMANT KHASRA NO 18//19 Rs. 15000 per sq yd")])
        self.assertIn("18//19", [x.value for x in values])
        self.assertFalse(any("Payment" in x.concept or "Owner" in x.concept for x in values))

