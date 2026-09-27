"""Source-section-gated extraction for unruled, repeated Award land grids.

The page OCR sometimes returns the whole printed header as one word and Table
Transformer merges RecNo with Khasra. This path uses the *printed* repeated
header and its horizontal extent, then keeps each physical schema independent.
It never repairs a Khasra or borrows a value from another row or group.
"""
from __future__ import annotations

from dataclasses import dataclass
import re


PRIMARY = "PRIMARY_AWARDED_LAND"
POSSESSION = "POSSESSION_RELATED"
COURT = "COURT"
CLAIMANT = "CLAIMANT"
CLASSIFICATION = "LAND_CLASSIFICATION"
UNKNOWN = "UNKNOWN"


def section_for_table(kind: str | None, land_layout: LandLayout | None) -> str:
    if kind == "CourtCwpTable":
        return COURT
    if kind == "WeakClaimTable":
        return CLAIMANT
    if kind == "LandClassification":
        return CLASSIFICATION
    if kind == "AwardLandTable" and land_layout is not None:
        return land_layout.section
    return UNKNOWN


@dataclass(frozen=True)
class LandLayout:
    section: str
    left: float
    right: float
    groups: int
    header_bottom: float
    page_width: float
    heading: str
    inherited: bool = False
    section_heading: str | None = None


def _box(word):
    return word.bounding_box


def _text(word):
    return " ".join(word.text.split())


def _header(words, page_height: float):
    # The real source prints RecNo/Khasra/Total Area/Area Awarded three times
    # on each main-schedule page. Annexure B prints two schemas with
    # "Awarded Area" instead. OCR may split or merge those labels.
    upper = [word for word in words if _box(word).y < page_height * .22]
    signals = [word for word in upper if re.search(r"khasra|killa|total\s*area|recorded\s*area|area\s*awarded|awarded\s*area", _text(word), re.I)]
    if not signals:
        return None
    # Headers are the left-to-right OCR fragments sharing one printed band.
    by_y = sorted(signals, key=lambda word: _box(word).y + _box(word).height / 2)
    bands: list[list] = []
    for word in by_y:
        cy = _box(word).y + _box(word).height / 2
        if bands and abs(cy - sum(_box(w).y + _box(w).height / 2 for w in bands[-1]) / len(bands[-1])) <= 24:
            bands[-1].append(word)
        else:
            bands.append([word])
    for band in sorted(bands, key=lambda group: -sum(_box(w).width for w in group)):
        line = " ".join(_text(word) for word in sorted(band, key=lambda w: _box(w).x))
        khasra = len(re.findall(r"khasra|killa", line, re.I))
        recorded = len(re.findall(r"total\s*area|recorded\s*area", line, re.I))
        awarded = len(re.findall(r"area\s*awarded|awarded\s*area", line, re.I))
        if not (khasra >= 1 and khasra == recorded == awarded):
            continue
        left = min(_box(w).x for w in band)
        right = max(_box(w).x + _box(w).width for w in band)
        bottom = max(_box(w).y + _box(w).height / 2 for w in band) + 17
        return line, khasra, left, right, bottom
    return None


def classify_land_section(words, image_width: int, image_height: int, previous: LandLayout | None = None) -> LandLayout | None:
    """Require an explicit section heading, or compatible proven continuation.

    No page number, village, Khasra, area or value is inherited. An unrelated
    heading or changed schema stops the continuation before interpretation.
    """
    header = _header(words, image_height)
    top = " ".join(_text(w) for w in sorted((w for w in words if _box(w).y < image_height * .2), key=lambda w: (_box(w).y, _box(w).x)))
    possession = bool(re.search(r"land\s+awarded\s+and\s+taken\s+over|annexure\s*[-' ]*b\b", top, re.I))
    primary = bool(re.search(r"\bland\s+awarded\b", top, re.I)) and not possession
    other = bool(re.search(r"\b(?:court\s+case|cwp\s+no|name\s+of\s+claimant|land\s+classification|block\s+b)\b", top, re.I))
    if not header:
        return None
    line, groups, left, right, bottom = header
    if possession:
        section = POSSESSION
        inherited = False
        section_heading = re.search(r"land\s+awarded\s+and\s+taken\s+over", top, re.I)
    elif primary and not other:
        section = PRIMARY
        inherited = False
        section_heading = re.search(r"\bland\s+awarded\b", top, re.I)
    elif previous and not other and groups == previous.groups and previous.section in (PRIMARY, POSSESSION):
        compatible = (abs(left / image_width - previous.left / previous.page_width) <= .035 and
                      abs(right / image_width - previous.right / previous.page_width) <= .035)
        if not compatible:
            return None
        section = previous.section
        inherited = True
        section_heading = None
    else:
        return None
    # A single weak header in unrelated prose cannot open a new Award grid.
    if groups < 2 or right - left < image_width * .6:
        return None
    return LandLayout(section, left, right, groups, bottom, image_width, line, inherited,
                      section_heading.group(0) if section_heading else None)


def _column_for(word, layout: LandLayout):
    center = _box(word).x + _box(word).width / 2
    stride = (layout.right - layout.left) / layout.groups
    if not layout.left <= center < layout.right:
        return None
    group = min(int((center - layout.left) / stride), layout.groups - 1)
    fraction = (center - layout.left - group * stride) / stride
    # RecNo is a separate heading row on this unruled layout, not a per-row
    # value to inherit. Khasra text can begin at the schema edge on later
    # pages, so a fixed left RecNo band would silently drop clear entries.
    if fraction < .35:
        role = "khasra"
    elif fraction < .67:
        role = "recordedArea"
    else:
        role = "awardedArea"
    return group, role


def _cell(word):
    box = _box(word)
    return {"text": _text(word), "region": {"x": box.x, "y": box.y, "width": box.width, "height": box.height}, "confidence": word.confidence}


def primary_land_candidates(page: int, words, image_height: int, layout: LandLayout) -> list[dict]:
    """Stage one strict identifier per physical row and logical schema.

    Area values are matched only in that schema and within a small same-row
    vertical window. Missing/ambiguous cells stay missing, with the raw page
    OCR and cell locator preserved for individual review.
    """
    if layout.section != PRIMARY:
        return []
    from benchmark.worker_semantics import award_candidate, strict_khasra

    columns: dict[tuple[int, str], list] = {(group, role): [] for group in range(layout.groups) for role in ("rectangle", "khasra", "recordedArea", "awardedArea")}
    for word in words:
        center_y = _box(word).y + _box(word).height / 2
        if center_y <= layout.header_bottom or center_y >= image_height - 115:
            continue
        column = _column_for(word, layout)
        if column is not None:
            columns[column].append(word)

    def cy(word):
        return _box(word).y + _box(word).height / 2

    candidates = []
    for group in range(layout.groups):
        identifiers = sorted(columns[group, "khasra"], key=cy)
        for index, token in enumerate(identifiers):
            if strict_khasra(_text(token))[0] is None:
                continue
            # Competing OCR tokens in the same physical cell are ambiguous;
            # do not concatenate or choose a plausible-looking identifier.
            neighbors = [w for w in identifiers if w is not token and abs(cy(w) - cy(token)) < 12]
            if neighbors:
                continue
            row_y = cy(token)
            roles = {"logicalGroupId": group + 1, "rectangle": group * 4,
                     "khasra": group * 4 + 1, "recordedArea": group * 4 + 2,
                     "awardedArea": group * 4 + 3}
            cells = {roles["khasra"]: _cell(token)}
            for role in ("rectangle", "recordedArea", "awardedArea"):
                nearby = [w for w in columns[group, role] if abs(cy(w) - row_y) <= 15]
                if len(nearby) == 1:
                    cells[roles[role]] = _cell(nearby[0])
            candidate = award_candidate(page, 1, round(row_y), cells, roles)
            if candidate is None:
                continue
            evidence = candidate["structuredPayload"]
            evidence.update({"sourceSection": PRIMARY, "sourceTableHeading": layout.heading,
                             "sourceSectionHeading": layout.section_heading,
                             "sourceSectionEvidence": "explicit heading" if not layout.inherited else "compatible continuation from preceding proven page",
                             "extractionMethod": "repeated-header OCR column geometry"})
            candidate["requiresIndividualReview"] = True
            candidate["interpretationWarnings"].append("Unruled table OCR has no independent cell-recognition agreement; verify identifier and both areas")
            candidates.append(candidate)
    return candidates


def primary_land_source_rows(page: int, words, image_height: int, layout: LandLayout, candidates: list[dict]) -> list[dict]:
    """Retain physical source slots without a strict identifier as review evidence.

    Area-column ink supplies the row positions; no missing identifier or area
    value is filled from neighboring rows, the village master, or a total.
    """
    if layout.section != PRIMARY:
        return []
    columns: dict[tuple[int, str], list] = {(group, role): [] for group in range(layout.groups)
                                           for role in ("khasra", "recordedArea", "awardedArea")}
    for word in words:
        cy = _box(word).y + _box(word).height / 2
        if not layout.header_bottom < cy < image_height - 115:
            continue
        column = _column_for(word, layout)
        if column in columns:
            columns[column].append(word)

    def cy(word):
        return _box(word).y + _box(word).height / 2

    source_rows = []
    stride = (layout.right - layout.left) / layout.groups
    for group in range(layout.groups):
        area_tokens = sorted((word for role in ("recordedArea", "awardedArea")
                              for word in columns[group, role]
                              if re.search(r"\d", _text(word)) and re.search(r"[-–—]", _text(word))), key=cy)
        clusters: list[list] = []
        for token in area_tokens:
            if clusters and cy(token) - cy(clusters[-1][0]) < 15:
                clusters[-1].append(token)
            else:
                clusters.append([token])
        for cluster in clusters:
            row_y = sum(cy(word) for word in cluster) / len(cluster)
            matched = [candidate for candidate in candidates
                       if candidate["structuredPayload"]["logicalGroupId"] == group + 1
                       and abs(candidate["sourceRegion"]["y"] + candidate["sourceRegion"]["height"] / 2 - row_y) <= 20]
            if matched:
                continue
            local = {role: [word for word in columns[group, role] if abs(cy(word) - row_y) <= 17]
                     for role in ("khasra", "recordedArea", "awardedArea")}
            source_rows.append({
                "sourceSection": PRIMARY, "page": page, "tableId": 1,
                "logicalGroupId": group + 1, "rowId": round(row_y),
                "identifierStatus": "REVIEW",
                "sourceRegion": {"x": layout.left + group * stride, "y": row_y - 17,
                                 "width": stride, "height": 34},
                "sourceCells": {role: [{"rawOcr": _text(word), "sourceRegion": _cell(word)["region"]}
                                       for word in local[role]]
                                for role in local},
            })
    return source_rows
