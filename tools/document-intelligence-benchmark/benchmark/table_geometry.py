"""Strict geometry-only join between OCR words and detected table cells."""
from __future__ import annotations

from collections import defaultdict

from .normalized import BoundingBox, Table, TableCell, Word


def _contains(box: BoundingBox, x: float, y: float) -> bool:
    return box.x <= x <= box.x + box.width and box.y <= y <= box.y + box.height


def _intersection(first: BoundingBox, second: BoundingBox) -> BoundingBox | None:
    left, top = max(first.x, second.x), max(first.y, second.y)
    right = min(first.x + first.width, second.x + second.width)
    bottom = min(first.y + first.height, second.y + second.height)
    if right <= left or bottom <= top:
        return None
    return BoundingBox(left, top, right - left, bottom - top)


def join_words_to_grid(
    *,
    table_box: BoundingBox,
    row_boxes: list[BoundingBox],
    column_boxes: list[BoundingBox],
    words: list[Word],
) -> tuple[Table, list[Word]]:
    """Return only unambiguous word-to-row-and-column assignments.

    A word must have its centre inside exactly one row and exactly one column.
    Ambiguous, out-of-grid, and non-table words are returned separately instead
    of being assigned based on proximity.
    """
    assigned: dict[tuple[int, int], list[Word]] = defaultdict(list)
    uncertain: list[Word] = []
    for word in words:
        box = word.bounding_box
        centre_x, centre_y = box.x + box.width / 2, box.y + box.height / 2
        if not _contains(table_box, centre_x, centre_y):
            continue
        rows = [index for index, row in enumerate(row_boxes) if _contains(row, centre_x, centre_y)]
        columns = [index for index, column in enumerate(column_boxes) if _contains(column, centre_x, centre_y)]
        if len(rows) != 1 or len(columns) != 1:
            uncertain.append(word)
            continue
        assigned[(rows[0], columns[0])].append(word)

    cells: list[TableCell] = []
    for (row, column), cell_words in sorted(assigned.items()):
        cell_box = _intersection(row_boxes[row], column_boxes[column])
        if cell_box is None:
            uncertain.extend(cell_words)
            continue
        ordered = sorted(cell_words, key=lambda word: (word.bounding_box.y, word.bounding_box.x))
        cells.append(TableCell(row, column, " ".join(word.text for word in ordered), cell_box))
    return Table(table_box, len(row_boxes), len(column_boxes), cells), uncertain
