"""Deterministic, auditable recognition crops for Award table cells.

This module intentionally knows nothing about an Award value.  It only applies
geometry-preserving image preparation, so it cannot turn one identifier into
another.  The raw crop is always retained alongside the normalized crop.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass
from hashlib import sha256
from typing import Mapping

from PIL import Image, ImageOps

PIPELINE_VERSION = "lac-cell-crop-v9.0"

ROLE_ALPHABETS = {
    "khasra": frozenset("0123456789/min "),
    "area": frozenset("0123456789- "),
    "rectangle": frozenset("0123456789"),
    "qualifier": frozenset("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ "),
}

def role_alphabet(role: str) -> frozenset[str]:
    """Recognizer decoding constraints; never a post-recognition repair."""
    return ROLE_ALPHABETS[role]


@dataclass(frozen=True)
class CropMetadata:
    pipelineVersion: str
    rawBox: dict
    rawSize: tuple[int, int]
    contentBox: tuple[int, int, int, int]
    normalizedSize: tuple[int, int]
    borderPixelsSuppressed: int
    rawSha256: str


def cell_identity(page: int, row: int, column: int) -> str:
    """Stable identity used by the train/test leakage guard."""
    return f"page:{page}:row:{row}:column:{column}"


def _clamped_box(image: Image.Image, box: Mapping[str, float], pad_x: int = 6, pad_y: int = 4) -> tuple[int, int, int, int]:
    left = max(0, int(box["x"]) - pad_x)
    top = max(0, int(box["y"]) - pad_y)
    right = min(image.width, int(box["x"] + box["width"]) + pad_x)
    bottom = min(image.height, int(box["y"] + box["height"]) + pad_y)
    if right <= left or bottom <= top:
        raise ValueError("cell box has no visible pixels")
    return left, top, right, bottom


def raw_cell_crop(page: Image.Image, box: Mapping[str, float]) -> tuple[Image.Image, tuple[int, int, int, int]]:
    """Return the evidence crop. No pixel is changed in this function."""
    actual = _clamped_box(page, box)
    return page.convert("RGB").crop(actual), actual


def _suppress_edge_rules(gray: Image.Image) -> tuple[Image.Image, int]:
    """Suppress only long, dark rules close to crop edges.

    Table Transformer supplies the cell geometry.  A rule is considered an
    edge rule only if it lies in a 4px edge band and occupies at least 70% of
    that band direction.  Thus central character strokes are never inspected
    or erased.
    """
    result = gray.copy()
    pixels = result.load()
    width, height = result.size
    changed = 0
    for y in range(min(4, height)):
        if sum(pixels[x, y] < 165 for x in range(width)) >= width * .70:
            for x in range(width):
                if pixels[x, y] != 255: pixels[x, y] = 255; changed += 1
    for y in range(max(0, height - 4), height):
        if sum(pixels[x, y] < 165 for x in range(width)) >= width * .70:
            for x in range(width):
                if pixels[x, y] != 255: pixels[x, y] = 255; changed += 1
    for x in range(min(4, width)):
        if sum(pixels[x, y] < 165 for y in range(height)) >= height * .70:
            for y in range(height):
                if pixels[x, y] != 255: pixels[x, y] = 255; changed += 1
    for x in range(max(0, width - 4), width):
        if sum(pixels[x, y] < 165 for y in range(height)) >= height * .70:
            for y in range(height):
                if pixels[x, y] != 255: pixels[x, y] = 255; changed += 1
    return result, changed


def _content_box(gray: Image.Image) -> tuple[int, int, int, int]:
    """Find ink after edge-rule suppression; keep a fixed safety margin."""
    w, h = gray.size
    points = [(x, y) for y in range(h) for x in range(w) if gray.getpixel((x, y)) < 190]
    if not points:
        return 0, 0, w, h
    xs, ys = zip(*points)
    margin = 3
    return max(0, min(xs) - margin), max(0, min(ys) - margin), min(w, max(xs) + margin + 1), min(h, max(ys) + margin + 1)


def normalize_cell_crop(raw: Image.Image, raw_box: Mapping[str, float]) -> tuple[Image.Image, CropMetadata]:
    """Normalize one raw crop without semantic or character-level editing."""
    gray = ImageOps.autocontrast(raw.convert("L"), cutoff=1)
    cleaned, suppressed = _suppress_edge_rules(gray)
    content = _content_box(cleaned)
    value = cleaned.crop(content)
    # Fixed text-height canvas, width remains proportional and is never stretched.
    target_height = 48
    scale = target_height / max(1, value.height)
    target_width = max(16, min(512, round(value.width * scale)))
    value = value.resize((target_width, target_height), Image.Resampling.LANCZOS)
    canvas = Image.new("L", (target_width + 12, target_height + 12), color=255)
    canvas.paste(value, (6, 6))
    digest = sha256(raw.convert("RGB").tobytes()).hexdigest()
    return canvas.convert("RGB"), CropMetadata(
        pipelineVersion=PIPELINE_VERSION,
        rawBox=dict(raw_box), rawSize=raw.size, contentBox=content,
        normalizedSize=canvas.size, borderPixelsSuppressed=suppressed, rawSha256=digest,
    )


def normalize_from_page(page: Image.Image, box: Mapping[str, float]) -> tuple[Image.Image, Image.Image, dict]:
    raw, actual = raw_cell_crop(page, box)
    normalized, metadata = normalize_cell_crop(raw, {**dict(box), "cropBox": actual})
    return raw, normalized, asdict(metadata)
