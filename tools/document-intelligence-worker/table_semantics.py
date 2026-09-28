"""Review-only source tables and Khasra mentions. No canonical Award fact is emitted."""
from __future__ import annotations

import re
from benchmark.worker_semantics import strict_khasra

VERSION = "table-source-rules/1.0"
UNKNOWN = "UNKNOWN_TABLE_SEMANTIC"


def _clean(value: str | None) -> str:
    return " ".join(str(value or "").split())


def _section(page: int, top: float, sections: list[dict]) -> tuple[int | None, dict | None]:
    eligible = [(index + 1, item) for index, item in enumerate(sections)
                if item["pageStart"] <= page <= item["pageEnd"]]
    preceding = [(index, item) for index, item in eligible
                 if any(e["page"] == page and e.get("sourceRegion") and
                        e["sourceRegion"]["y"] <= top for e in item["evidence"])]
    if preceding:
        return max(preceding, key=lambda pair: max(e["sourceRegion"]["y"] for e in pair[1]["evidence"]
                                                if e["page"] == page and e.get("sourceRegion") and
                                                e["sourceRegion"]["y"] <= top))
    return eligible[0] if len(eligible) == 1 else (None, None)


def _meaning(section: dict | None, heading: str, columns: dict[int, str], rows: dict[int, dict], header_row: int | None) -> str:
    if section is None or section["semantic"] == "UNKNOWN":
        return UNKNOWN
    h = heading.casefold()
    labels = " ".join(columns.values()).casefold()
    context = section["semantic"]
    # A source can have contradictory headings/roles. Abstain, never vote.
    signals = [bool(re.search(pattern, h)) for pattern in
               (r"land awarded", r"claims? (?:were|filed)|claimant", r"notification|specification",
                r"owner|ownership", r"cwp|court|dispute")]
    if sum(signals) > 1 and not re.search(r"land awarded and taken over", h):
        return UNKNOWN
    if context == "POSSESSION_REFERENCE_OR_SECTION" and re.search(r"land awarded and taken over|possession", h):
        return "POSSESSION_LAND"
    if context == "LAND_SCHEDULE" and re.search(r"true and correct area", h) and \
            re.search(r"notification", labels) and re.search(r"field book", labels):
        return "TRUE_CORRECT_AREA"
    if context == "LAND_SCHEDULE" and re.fullmatch(r"(?:annexure\s*[- ]?[a-z]\s+)?land awarded", h.strip(" .:-")):
        return "AWARDED_LAND"
    if context == "STATUTORY_NOTIFICATIONS" and re.search(r"notification.*specification", h) and \
            re.search(r"field\s*(?:nos?|numbers?)|boundar", labels):
        return "NOTIFIED_LAND"
    if context == "CLAIMS" and re.search(r"claim", h) and re.search(r"claimant", labels):
        return "CLAIM_LINKED_LAND"
    if context == "OTHER" and re.search(r"owner|ownership", h) and \
            re.search(r"owner", labels) and re.search(r"occupant|soil", labels):
        return "OWNER_LINKED_LAND"
    if context == "COURT_OR_DISPUTE_REFERENCE" and re.search(r"cwp|court", h) and \
            re.search(r"status", labels) and re.search(r"cwp|case", labels):
        status_columns = [i for i, text in columns.items() if re.search(r"status", text, re.I)]
        if any(re.search(r"status\s*quo\s*dispossession|stay\s+(?:order|granted|operates)",
                         str(row.get(col, {}).get("text", "")), re.I)
               for row_id, row in rows.items() if row_id != header_row for col in status_columns):
            return "STAY_AFFECTED_LAND"
    return UNKNOWN


def _layout(columns: list[str], hint: str | None) -> str:
    if hint:
        return hint
    labels = " ".join(columns).casefold()
    if "field book" in labels and "notification" in labels:
        return "COMPARATIVE_MULTI_COLUMN"
    if len(columns) == 2:
        return "SIMPLE_TWO_COLUMN"
    if len(columns) >= 3:
        return "EXPANDED_GRID"
    return "UNKNOWN"


def _area_role(header: str, semantic: str) -> str:
    h = header.casefold()
    if "field book" in h and semantic == "TRUE_CORRECT_AREA":
        return "CORRECTED"
    if "notification" in h and semantic in ("TRUE_CORRECT_AREA", "NOTIFIED_LAND"):
        return "NOTIFIED"
    if "awarded" in h:
        return "AWARDED"
    if "recorded" in h or "total area" in h and semantic == "AWARDED_LAND":
        return "RECORDED"
    if "possession" in h or "taken over" in h:
        return "POSSESSED"
    if semantic == "CLAIM_LINKED_LAND" and h.strip() == "area":
        return "CLAIMED"
    return "UNKNOWN"


def classify_table_region(page: int, table_id: int, box: dict, headers: dict[int, str], rows: dict[int, dict],
                          sections: list[dict], header_row: int | None = None,
                          layout_hint: str | None = None) -> tuple[dict | None, list[dict]]:
    """Use one existing geometry grid; never derive a business meaning from shape alone."""
    labels = {col: _clean(value) for col, value in headers.items() if _clean(value)}
    khasra_cols = [col for col, label in sorted(labels.items())
                   if re.search(r"\bkhasra\b|\bkilla\b|\bfield\s*(?:nos?|numbers?)\b", label, re.I)]
    if not khasra_cols or not rows:
        return None, []
    section_index, section = _section(page, box["y"], sections)
    heading = _clean(section.get("rawHeading")) if section else ""
    column_texts = [labels[col] for col in sorted(labels)]
    column_sources = [{"column": col, "rawText": labels[col],
                       "sourceRegion": rows.get(header_row, {}).get(col, {}).get("region") or box}
                      for col in sorted(labels)]
    semantic = _meaning(section, heading, labels, rows, header_row)
    same_page_heading = section and any(e["page"] == page and e["rawText"] == heading for e in section["evidence"])
    continuation = (section and section["pageStart"] < page and
                    next((e for e in section["evidence"] if e["page"] == page and
                          re.search(r"\bcont(?:inued|d\.?)\b", e["rawText"], re.I)), None))
    if semantic != UNKNOWN and not (same_page_heading or continuation):
        semantic = UNKNOWN
    table_key = f"p{page}-t{table_id}"
    evidence = []
    if section and heading:
        source = next((e for e in section["evidence"] if e["rawText"] == heading), None)
        if source:
            evidence.append(source)
        if continuation:
            evidence.append(continuation)
    evidence.extend({"page": page, "rawText": item["rawText"][:220], "sourceRegion": item["sourceRegion"]}
                    for item in column_sources[:8])
    if semantic == "STAY_AFFECTED_LAND":
        for row_id, cells in sorted(rows.items()):
            if row_id == header_row:
                continue
            found = next((cell for col, cell in cells.items() if re.search(r"status", labels.get(col, ""), re.I) and
                          re.search(r"status\s*quo\s*dispossession|stay\s+(?:order|granted|operates)",
                                    str(cell.get("text", "")), re.I)), None)
            if found:
                evidence.append({"page": page, "rawText": _clean(found["text"])[:220],
                                 "sourceRegion": found.get("region") or box})
                break
    if not evidence:
        return None, []
    observation = {"observationType": "DocumentTableSemantic", "tableId": table_key,
        "pageStart": page, "pageEnd": page, "sectionObservationIndex": section_index,
        "rawHeading": heading or None,
        "rawColumnLabels": column_sources,
        "layout": _layout(column_texts, layout_hint), "semantic": semantic,
        "confidence": .86 if semantic != UNKNOWN else 0.0, "requiresHumanReview": True,
        "sourceRegion": box, "evidence": evidence,
        "warnings": ["Source table meaning requires review; layout and Khasra columns are not Award facts."
                     if semantic != UNKNOWN else "Table meaning is uncertain or section context is unknown."],
        "classifierVersion": VERSION}
    occurrences = []
    ordered_cols = sorted(labels)
    for row_id, cells in sorted(rows.items()):
        if row_id == header_row:
            continue
        row_context = _clean(" | ".join(str(cells[col].get("text", "")) for col in sorted(cells)))[:500]
        row_semantic = semantic
        if semantic == "STAY_AFFECTED_LAND" and not any(
            re.search(r"status\s*quo\s*dispossession|stay\s+(?:order|granted|operates)",
                      str(cells.get(col, {}).get("text", "")), re.I)
            for col, label in labels.items() if re.search(r"status", label, re.I)):
            row_semantic = UNKNOWN
        for column in khasra_cols:
            source = cells.get(column)
            if not source or not _clean(source.get("text")):
                continue
            next_khasra = next((c for c in khasra_cols if c > column), max(ordered_cols, default=column) + 1)
            area_fields = []
            for other in ordered_cols:
                if not column < other < next_khasra or not re.search(r"\barea\b", labels[other], re.I):
                    continue
                area = cells.get(other)
                if area and _clean(area.get("text")):
                    area_fields.append({"sourceColumn": other, "rawText": _clean(area["text"])[:100],
                                        "role": _area_role(labels[other], row_semantic),
                                        "sourceRegion": area.get("region") or box})
            raw_cell = _clean(source["text"])
            mentions = [_clean(part) for part in re.split(r"[,;]", raw_cell) if _clean(part)]
            for ordinal, raw in enumerate(mentions, 1):
                if len(raw) > 100:
                    continue
                normalized, qualifier = strict_khasra(raw)
                occurrences.append({"observationType": "KhasraOccurrence",
                    "occurrenceId": f"{table_key}-r{row_id}-c{column}-m{ordinal}",
                    "tableId": table_key, "page": page, "sourceRow": row_id, "sourceColumn": column,
                    "mentionIndex": ordinal, "rawKhasraText": raw,
                    "normalizedKhasraNumber": normalized, "qualifier": qualifier,
                    "explicitPart": bool(re.search(r"\bpart\b", raw, re.I)),
                    "semantic": row_semantic, "rawRowContext": row_context or raw,
                    "sourceRegion": source.get("region") or box, "areaFields": area_fields,
                    "confidence": .75 if normalized and row_semantic != UNKNOWN else 0.0,
                    "requiresHumanReview": True,
                    "evidence": [{"page": page, "rawText": raw, "sourceRegion": source.get("region") or box}],
                    "warnings": ["Exact source mention for review; no canonical Khasra or Award link."
                                 if normalized else "Khasra text is unresolved; no digit or separator was repaired."],
                    "classifierVersion": VERSION})
    return observation, occurrences


def classify_inline_page(page: int, lines: list[dict], sections: list[dict], table_id: int = 1) -> tuple[dict | None, list[dict]]:
    """Only an explicit notification specification's field-number list is supported."""
    for index, line in enumerate(lines):
        match = re.search(r"\bfield\s*(?:nos?\.?|numbers?)\s*(?:or\s+boundaries)?\b", line["text"], re.I)
        if not match or not line.get("sourceRegion"):
            continue
        raw = _clean(line["text"][match.end():].lstrip(" :.-"))
        source = line
        if not raw and index + 1 < len(lines):
            source = lines[index + 1]
            raw = _clean(source["text"])
        if not raw or not re.search(r"\d+\s*/\s*\d+", raw) or not source.get("sourceRegion"):
            continue
        box = source["sourceRegion"]
        headers = {0: _clean(match.group(0))}
        rows = {0: {0: {"text": headers[0], "region": line["sourceRegion"]}},
                1: {0: {"text": raw, "region": box}}}
        observation, occurrences = classify_table_region(page, table_id, box, headers, rows, sections, 0,
            "INLINE_PARAGRAPH_LIST")
        if observation:
            return observation, occurrences
    return None, []
