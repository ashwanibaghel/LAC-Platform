import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from benchmark.normalized import BoundingBox, Word
from benchmark.table_geometry import join_words_to_grid


class GeometryJoinTests(unittest.TestCase):
    def test_joins_only_a_word_in_one_row_and_one_column(self):
        table, uncertain = join_words_to_grid(
            table_box=BoundingBox(0, 0, 100, 100),
            row_boxes=[BoundingBox(0, 0, 100, 50), BoundingBox(0, 50, 100, 50)],
            column_boxes=[BoundingBox(0, 0, 50, 100), BoundingBox(50, 0, 50, 100)],
            words=[Word("Khasra", BoundingBox(5, 10, 20, 10)), Word("0-14", BoundingBox(60, 60, 20, 10))],
        )
        self.assertEqual([(cell.row, cell.column, cell.text) for cell in table.cells], [(0, 0, "Khasra"), (1, 1, "0-14")])
        self.assertEqual(uncertain, [])

    def test_does_not_guess_for_overlapping_rows(self):
        _, uncertain = join_words_to_grid(
            table_box=BoundingBox(0, 0, 100, 100),
            row_boxes=[BoundingBox(0, 0, 100, 60), BoundingBox(0, 40, 100, 60)],
            column_boxes=[BoundingBox(0, 0, 100, 100)],
            words=[Word("uncertain", BoundingBox(5, 45, 20, 10))],
        )
        self.assertEqual([word.text for word in uncertain], ["uncertain"])
