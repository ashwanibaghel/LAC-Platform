import sys
import unittest
from pathlib import Path
from types import SimpleNamespace

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "document-intelligence-worker"))
from claimant_schedule import claimed_land_rate, detect_header, extract_claimant_tables


def word(text, x, y, width=75, height=20):
    return SimpleNamespace(text=text, confidence=.92,
                           bounding_box=SimpleNamespace(x=x, y=y, width=width, height=height))


def header():
    return [word("S.NO", 100, 10, 35), word("NAME OF CLAIMANT", 150, 10, 180),
            word("KHASRA NO", 400, 10, 100), word("AREA", 600, 10, 45),
            word("CLAIM", 750, 10, 70)]


def row(number, y, claim="Rs.3000/- per sq yards for land"):
    return [word(str(number), 105, y, 30), word(f"Person {number} s/o Parent", 160, y, 150),
            word("at recorded address", 160, y + 16, 145),
            word("12//20/2 etc.", 415, y, 115), word("9-18", 610, y, 50),
            word(claim, 755, y, 180), word("for boundary wall", 755, y + 16, 155)]


class ClaimantScheduleTests(unittest.TestCase):
    def test_requires_five_ordered_source_headers(self):
        self.assertIsNotNone(detect_header(header(), 1100))
        self.assertIsNone(detect_header(header()[:-1], 1100))
        reversed_header = header()
        reversed_header[2].bounding_box.x = 800
        self.assertIsNone(detect_header(reversed_header, 1100))

    def test_claimant_header_after_other_tables_on_same_page(self):
        earlier = [word("S.NO", 100, 10, 35), word("KHASRA NO", 400, 10, 100),
                   word("AREA", 600, 10, 45)]
        lower = header()
        for item in lower:
            item.bounding_box.y = 600
        claims, consumed, _, stats = extract_claimant_tables(
            7, earlier + lower + row(1, 640) + row(2, 700), 1100)
        self.assertEqual(2, stats["rowsDetected"])
        self.assertEqual(["1", "2"], [item["structuredPayload"]["sourceSerialNumber"] for item in claims])
        self.assertFalse(any(id(item) in consumed for item in earlier))

    def test_wrapped_fields_stay_in_one_source_row(self):
        words = header() + row(3, 48) + row(4, 105, "Rs.5000/- per sq yrd for land")
        claims, consumed, layout, stats = extract_claimant_tables(8, words, 1100)
        self.assertEqual(2, stats["rowsDetected"])
        self.assertEqual(2, len(claims))
        first = claims[0]["structuredPayload"]
        self.assertEqual("3", first["sourceSerialNumber"])
        self.assertEqual("Person 3 s/o Parent at recorded address", first["claimantText"])
        self.assertEqual("12//20/2 etc.", first["khasraReferences"])
        self.assertEqual("9-18", first["claimedAreaText"])
        self.assertIn("for boundary wall", first["claimText"])
        self.assertEqual(("3000", "sq yard"), (first["claimedRateAmount"], first["claimedRateUnit"]))
        self.assertEqual("5000", claims[1]["structuredPayload"]["claimedRateAmount"])
        self.assertTrue(consumed)
        self.assertTrue(claims[0]["requiresIndividualReview"])
        self.assertIsNotNone(claims[0]["sourceRegion"])
        self.assertIsNotNone(first["sourceCells"]["claim"]["sourceRegion"])

    def test_160_rows_across_unheaded_continuation_pages(self):
        previous = None
        numbers = []
        for page in range(8, 16):
            start = (page - 8) * 20 + 1
            words = (header() if page == 8 else []) + [
                item for number in range(start, start + 20)
                for item in row(number, 48 + (number - start) * 50)]
            claims, _, previous, stats = extract_claimant_tables(page, words, 1100, previous)
            self.assertEqual(20, stats["rowsDetected"])
            numbers.extend(int(claim["structuredPayload"]["sourceSerialNumber"]) for claim in claims)
        self.assertEqual(list(range(1, 161)), numbers)

    def test_continuation_does_not_attach_unrelated_numbered_page(self):
        _, _, layout, _ = extract_claimant_tables(8, header() + row(3, 48) + row(4, 100), 1100)
        claims, _, next_layout, _ = extract_claimant_tables(9, row(20, 50) + row(21, 100), 1100, layout)
        self.assertEqual([], claims)
        self.assertIsNone(next_layout)

    def test_last_single_row_stops_before_full_width_prose(self):
        _, _, layout, _ = extract_claimant_tables(8, header() + row(161, 48) + row(162, 100), 1100)
        words = row(163, 48) + [word("The claimants filed documents as evidence", 155, 125, 760)]
        claims, _, _, stats = extract_claimant_tables(9, words, 1100, layout)
        self.assertEqual(1, stats["rowsDetected"])
        self.assertEqual(1, len(claims))
        self.assertNotIn("documents", claims[0]["rawSourceText"])

    def test_rate_is_not_guessed_or_collapsed_with_other_components(self):
        self.assertEqual((None, None), claimed_land_rate("Rs.3000/- per sq yard and Rs.5000/- per sq yard"))
        self.assertEqual((None, None), claimed_land_rate("Rs.5 lacs for boundary wall"))
        self.assertEqual(("3000", "sq yard"), claimed_land_rate("Rs.3000/- per sq yrds for land; Rs.5 lacs for wall"))
        self.assertEqual(("15000", "sq yard"), claimed_land_rate("Rs.15,000/- per sq yds. as well as structures"))


if __name__ == "__main__":
    unittest.main()
