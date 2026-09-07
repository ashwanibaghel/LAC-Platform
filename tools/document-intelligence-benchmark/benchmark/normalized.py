"""Neutral, engine-independent document model used only by the benchmark."""
from __future__ import annotations

from dataclasses import asdict, dataclass, field
from typing import Literal

BlockType = Literal["Heading", "Paragraph", "Table", "Image", "Other"]


@dataclass(frozen=True)
class BoundingBox:
    x: float
    y: float
    width: float
    height: float


@dataclass(frozen=True)
class Word:
    text: str
    bounding_box: BoundingBox
    confidence: float | None = None


@dataclass(frozen=True)
class Block:
    type: BlockType
    text: str
    bounding_box: BoundingBox


@dataclass(frozen=True)
class TableCell:
    row: int
    column: int
    text: str
    bounding_box: BoundingBox | None = None


@dataclass(frozen=True)
class Table:
    bounding_box: BoundingBox
    rows: int
    columns: int
    cells: list[TableCell] = field(default_factory=list)


@dataclass(frozen=True)
class DocumentPage:
    page_number: int
    width: float
    height: float
    blocks: list[Block] = field(default_factory=list)
    words: list[Word] = field(default_factory=list)
    tables: list[Table] = field(default_factory=list)

    def to_dict(self) -> dict:
        return asdict(self)
