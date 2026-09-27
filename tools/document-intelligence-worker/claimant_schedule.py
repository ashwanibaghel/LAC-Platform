"""Source-first extraction of Award claimant schedules from OCR geometry.

The five-column header establishes the schema. Later pages may reuse that
schema only while serial anchors continue in the same column positions.
No person, parcel, payment, or entitlement relationship is inferred here.
"""
from __future__ import annotations

import re
from dataclasses import dataclass


@dataclass(frozen=True)
class ClaimantLayout:
    boundaries: tuple[float, float, float, float, float, float]
    page_width: float
    last_serial: int | None = None


def _box(word) -> dict:
    b = word.bounding_box
    return {"x": float(b.x), "y": float(b.y), "width": float(b.width), "height": float(b.height)}


def _centre_x(word) -> float:
    b = word.bounding_box
    return b.x + b.width / 2


def _column(word, boundaries: tuple[float, ...]) -> int | None:
    x = _centre_x(word)
    return next((i for i in range(5) if boundaries[i] <= x < boundaries[i + 1]), None)


def _union(words) -> dict | None:
    if not words:
        return None
    boxes = [_box(word) for word in words]
    left, top = min(b["x"] for b in boxes), min(b["y"] for b in boxes)
    right = max(b["x"] + b["width"] for b in boxes)
    bottom = max(b["y"] + b["height"] for b in boxes)
    return {"x": left, "y": top, "width": right - left, "height": bottom - top}


def _text(words) -> str | None:
    value = " ".join(word.text for word in sorted(words, key=lambda w: (w.bounding_box.y, w.bounding_box.x))).strip()
    return value or None


def _header_role(value: str) -> str | None:
    text = re.sub(r"\s+", " ", value.upper()).strip()
    if re.fullmatch(r"(?:S\.?\s*NO\.?|SR\.?\s*NO\.?|SERIAL\s*(?:NO\.?|NUMBER))", text):
        return "serial"
    if re.fullmatch(r"(?:NAME\s+OF\s+CLAIMANT|CLAIMANT)", text):
        return "claimant"
    if re.fullmatch(r"KHASRA\s*(?:NO\.?|NUMBER)", text):
        return "khasra"
    if text == "AREA":
        return "area"
    if text == "CLAIM":
        return "claim"
    return None


def detect_header(words, page_width: float) -> tuple[ClaimantLayout, float] | None:
    """Accept only an ordered, compact five-role source header."""
    order = ("serial", "claimant", "khasra", "area", "claim")
    # A claimant header can follow another table (including its own S.NO and
    # AREA headings) on the same page. Anchor on CLAIMANT and select roles
    # from that physical header band instead of the first occurrence on page.
    values = None
    for anchor in (word for word in words if _header_role(word.text) == "claimant"):
        roles = {"claimant": anchor}
        for role in order:
            if role == "claimant":
                continue
            matches = [word for word in words if _header_role(word.text) == role
                       and abs(word.bounding_box.y - anchor.bounding_box.y) <= 45]
            if matches:
                roles[role] = min(matches, key=lambda word: abs(word.bounding_box.y - anchor.bounding_box.y))
        if set(roles) == set(order):
            values = [roles[role] for role in order]
            break
    if values is None:
        return None
    xs = [_centre_x(value) for value in values]
    ys = [value.bounding_box.y for value in values]
    if xs != sorted(xs) or max(ys) - min(ys) > 45:
        return None
    boundaries = [max(0, values[0].bounding_box.x - 35)]
    for left, right in zip(values, values[1:]):
        boundaries.append((left.bounding_box.x + left.bounding_box.width + right.bounding_box.x) / 2)
    boundaries.append(min(page_width, values[-1].bounding_box.x + values[-1].bounding_box.width + 100))
    if any(b >= c for b, c in zip(boundaries, boundaries[1:])):
        return None
    return ClaimantLayout(tuple(boundaries), page_width), min(ys) + 15


_RATE = re.compile(r"\b(?:RS\.?|INR|₹)\s*(\d{1,3}(?:,\d{3})*|\d{4,7})(?:\s*/-)?\s*per\s*sq\.?\s*y(?:ard|rd|d)s?\b", re.I)


def claimed_land_rate(claim_text: str | None) -> tuple[str | None, str | None]:
    """Parse an explicit square-yard rate; never select between two rates."""
    if not claim_text:
        return None, None
    values = {match.group(1).replace(",", "") for match in _RATE.finditer(claim_text)}
    return (next(iter(values)), "sq yard") if len(values) == 1 else (None, None)


def _field(words, cell_region: dict) -> dict:
    raw = _text(words)
    return {"rawOcr": raw, "normalizedSuggestion": raw, "sourceRegion": cell_region, "ocrRegion": _union(words)}


def extract_claimant_tables(page: int, words, page_width: float, previous: ClaimantLayout | None = None):
    """Return one Claim per serial-anchored physical row and consumed OCR words.

    An unheaded continuation is accepted only directly after an established
    table, with aligned columns and continuing serial numbers. Page portions
    are never merged into a previous page's candidate.
    """
    header = detect_header(words, page_width)
    if header:
        layout, minimum_y = header
    elif previous and abs(page_width - previous.page_width) <= previous.page_width * .03:
        layout, minimum_y = previous, 0
    else:
        return [], set(), None, {"rowsDetected": 0, "incompleteRows": 0}
    boundaries = layout.boundaries
    serials = sorted(
        (word for word in words if _column(word, boundaries) == 0
         and word.bounding_box.y >= minimum_y
         and re.fullmatch(r"\d{1,3}", word.text.strip())
         and 1 <= int(word.text) <= 999),
        key=lambda word: word.bounding_box.y,
    )
    if not header:
        if previous is None or previous.last_serial is None or not serials:
            return [], set(), None, {"rowsDetected": 0, "incompleteRows": 0}
        first = int(serials[0].text)
        if not previous.last_serial < first <= previous.last_serial + 3:
            return [], set(), None, {"rowsDetected": 0, "incompleteRows": 0}
    accepted = []
    last = previous.last_serial if not header and previous else None
    for word in serials:
        number = int(word.text)
        if last is not None and not last < number <= last + 3:
            continue
        accepted.append(word)
        last = number
    if not accepted or (header and len(accepted) < 2):
        return [], set(), None, {"rowsDetected": 0, "incompleteRows": 0}
    if len(accepted) == 1 and (not previous or previous.last_serial is None or int(accepted[0].text) != previous.last_serial + 1):
        return [], set(), None, {"rowsDetected": 0, "incompleteRows": 0}
    # Require the other four columns on this same page. A numbered prose list
    # does not become a claimant schedule merely by continuing the sequence.
    data_words = [word for word in words if accepted[0].bounding_box.y <= word.bounding_box.y
                  and (len(accepted) > 1 or word.bounding_box.y < accepted[0].bounding_box.y + 120)]
    if len({_column(word, boundaries) for word in data_words}) < 5:
        return [], set(), None, {"rowsDetected": 0, "incompleteRows": 0}
    candidates, consumed = [], set()
    incomplete = 0
    for index, anchor in enumerate(accepted):
        top = anchor.bounding_box.y - 2
        bottom = accepted[index + 1].bounding_box.y - 2 if index + 1 < len(accepted) else (
            anchor.bounding_box.y + 120 if len(accepted) == 1 else float("inf"))
        # A full-width prose line after the final table row marks the end of
        # this schedule; it is not another wrapped claimant cell.
        if index + 1 == len(accepted):
            prose_starts = [word.bounding_box.y for word in words
                            if word.bounding_box.y > anchor.bounding_box.y + 25
                            and word.bounding_box.y < bottom
                            and sum(word.bounding_box.x < edge < word.bounding_box.x + word.bounding_box.width
                                    for edge in boundaries[1:-1]) >= 2]
            if prose_starts:
                bottom = min(bottom, min(prose_starts))
        row_words = [word for word in words if top <= word.bounding_box.y < bottom and _column(word, boundaries) is not None]
        cells = {role: [word for word in row_words if _column(word, boundaries) == col]
                 for col, role in enumerate(("serial", "claimant", "khasra", "area", "claim"))}
        cells["serial"] = [anchor]
        missing_source = not cells["claimant"] or not cells["claim"]
        if missing_source:
            incomplete += 1
        observed_bottom = max((word.bounding_box.y + word.bounding_box.height for word in row_words), default=top + 25)
        row_bottom = min(bottom, observed_bottom + 5) if bottom == float("inf") else bottom
        row_height = max(20, row_bottom - top)
        # The next serial anchor begins just below the row border. A small
        # crop margin keeps descenders and the final wrapped word visible.
        crop_height = row_height + (6 if index + 1 < len(accepted) else 0)
        source_cells = {role: _field(value, {"x": boundaries[col], "y": top,
                                            "width": boundaries[col + 1] - boundaries[col],
                                            "height": crop_height})
                        for col, (role, value) in enumerate(cells.items())}
        claim_text = source_cells["claim"]["rawOcr"]
        rate, unit = claimed_land_rate(claim_text)
        raw = _text(row_words) or ""
        payload = {
            "sourceSerialNumber": anchor.text,
            "claimantText": source_cells["claimant"]["rawOcr"],
            "khasraReferences": source_cells["khasra"]["rawOcr"],
            "claimedAreaText": source_cells["area"]["rawOcr"],
            "claimText": claim_text,
            "claimedRateAmount": rate,
            "claimedRateUnit": unit,
            "claimedAmount": None,
            "sourceCells": source_cells,
        }
        candidates.append({
            "candidateType": "Claim",
            "structuredPayload": payload,
            "page": page,
            "sourceRegion": {"x": boundaries[0], "y": top,
                             "width": boundaries[-1] - boundaries[0], "height": crop_height},
            "rawSourceText": raw,
            "rawOcr": raw,
            "normalizedSuggestion": None,
            "normalizationReason": None,
            "confidence": min((word.confidence for word in row_words if word.confidence is not None), default=None),
            "requiresIndividualReview": True,
            "interpretationWarnings": ["Claimant is source identity only, not an owner or payee; claim rate is not awarded or paid."]
                + (["One or more claimant/claim cells were unreadable; correct from the PDF before confirmation."] if missing_source else []),
        })
        consumed.update(id(word) for word in row_words)
    updated = ClaimantLayout(boundaries, page_width, int(accepted[-1].text))
    if header:
        header_y = minimum_y - 15
        consumed.update(id(word) for word in words if _header_role(word.text) is not None
                        and abs(word.bounding_box.y - header_y) <= 45
                        and boundaries[0] <= _centre_x(word) < boundaries[-1])
    return candidates, consumed, updated, {"rowsDetected": len(accepted), "incompleteRows": incomplete}
