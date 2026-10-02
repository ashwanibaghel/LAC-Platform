import sys
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from inventory_workbook import select_rows

HEADERS = ["Case Satus", "Case No.", "Which Court Pertains to ", "Last order link"]


class InventoryTests(unittest.TestCase):
    def test_status_aliases_and_generic_court_are_not_silently_dropped(self):
        rows, exceptions = select_rows("Register", [[], HEADERS,
            ["PENDING", "WPC 1/2025", "Delhi High Court", ""],
            ["Diposed off", "WPC 2/2025", "Delhi High Court", ""],
            ["Disposed off", "WPC 3/2025", "High Court", ""],
            [None, "CM ___/2025", "Delhi High Court", ""],
            ["Pending", "WPC 4/2025", "Supreme Court", ""]])
        self.assertEqual(len(rows), 3)
        self.assertEqual([r["register_status"] for r in rows], ["Pending", "Disposed", "Disposed"])
        self.assertEqual(rows[2]["court_state"], "CourtNeedsConfirmation")
        self.assertEqual(len(exceptions), 1)
        self.assertTrue(all(r["annotation_state"] == "UNREVIEWED" for r in rows))

    def test_missing_and_duplicate_identity_preserve_every_source_row(self):
        rows, _ = select_rows("Register", [HEADERS,
            ["Pending", "WPC 1/2025", "Delhi High Court", ""],
            ["Pending", "WPC 1/2025", "Delhi High Court", ""],
            ["disposed off", None, "Delhi High Court", ""],
            ["Pending", "WPC No.      /2026", "Delhi High Court", ""]])
        self.assertEqual(len(rows), 4)
        self.assertIn("RepeatedRawCaseNumber", rows[0]["issues"])
        self.assertIn("MissingCaseNumber", rows[2]["issues"])
        self.assertIn("IncompleteCaseNumber", rows[3]["issues"])

    def test_wrong_sheet_and_repeated_header_fail_closed(self):
        with self.assertRaises(ValueError):
            select_rows("Other", [["Private office notes"]])
        with self.assertRaises(ValueError):
            select_rows("Register", [HEADERS, HEADERS])


if __name__ == "__main__":
    unittest.main()
