"""Regression coverage for page parity, physical clearance and print decorations."""
import unittest
from verify_green_legal_pdf import geometry_failures, neutral_color


class PhysicalGeometryTests(unittest.TestCase):
    def row(self, page=1):
        return dict(page=page, widthPt=612, heightPt=1008,
                    inkClearanceMm=dict(left=45 if page % 2 else 15,
                                        right=15 if page % 2 else 45, top=25, bottom=25))

    def test_odd_and_even_boundaries(self):
        self.assertEqual(geometry_failures(self.row(1)), [])
        self.assertEqual(geometry_failures(self.row(2)), [])
        row = self.row(2)
        row['inkClearanceMm']['right'] = 15
        self.assertTrue(geometry_failures(row))

    def test_rejects_actual_bottom_ink_intrusion(self):
        row = self.row()
        row['inkClearanceMm']['bottom'] = 24.24
        self.assertIn('bottom ink clearance is below 25 mm', geometry_failures(row))

    def test_font_box_is_not_printed_ink(self):
        row = self.row()
        row['fontBoxBottomMm'] = 24.24
        row['inkClearanceMm']['bottom'] = 25.18
        self.assertEqual(geometry_failures(row), [])

    def test_rejects_letter_paper_and_accepts_twip_rounding_only(self):
        row = self.row()
        row['heightPt'] = 792
        self.assertTrue(geometry_failures(row))
        row = self.row()
        row['inkClearanceMm']['left'] = 44.997
        self.assertEqual(geometry_failures(row), [])
        row['inkClearanceMm']['left'] = 44.9
        self.assertTrue(geometry_failures(row))

    def test_rejects_red_green_and_unknown_print_colors(self):
        for color in ((1, 0, 0), (0, 1, 0), (1, 0, 1, 0), 'Pattern'):
            self.assertFalse(neutral_color(color))
        for color in (None, 0, (0,), (0, 0, 0), (.5, .5, .5), (0, 0, 0, 1)):
            self.assertTrue(neutral_color(color))


if __name__ == '__main__':
    unittest.main()
