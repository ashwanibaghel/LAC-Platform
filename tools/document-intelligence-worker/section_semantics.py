"""Source-heading section observations for V2 Award PDFs; never business facts."""
from __future__ import annotations

import re

CLASSIFIER_VERSION = "document-section-rules/1.0"


def _heading_semantic(raw: str) -> str | None:
    text = " ".join(raw.casefold().split()).strip(" .:-")
    if not text or len(text) > 110:
        return None
    # Specific headings precede broad ones. A body mention is not a heading.
    if re.fullmatch(r"(?:annexure\s*[- ]?[a-z]\s+)?land awarded and taken over", text):
        return "POSSESSION_REFERENCE_OR_SECTION"
    if re.fullmatch(r"(?:annexure\s*[- ]?[a-z]\s+)?land awarded|true and correct area|land schedule", text):
        return "LAND_SCHEDULE"
    if re.fullmatch(r"(?:award\s*(?:no\.?|number)\s*[:#-]?\s*\S+|award identity)", text):
        return "AWARD_IDENTITY"
    if re.fullmatch(r"(?:statutory\s+)?notifications?|notification\s+(?:u/s|under section)\s+(?:4|6|17)", text):
        return "STATUTORY_NOTIFICATIONS"
    if re.fullmatch(r"(?:delhi administration\s+)?notification\s*[-—:]\s*specification", text):
        return "STATUTORY_NOTIFICATIONS"
    if re.fullmatch(r"a statement showing the details of khasra nos\.?[,]? ownership[,]? area and classification of soil", text):
        return "OTHER"
    if re.fullmatch(r"claims?|claimant claims|the following claims were filed(?: in response to notices u/s 9 & 10)?", text):
        return "CLAIMS"
    if re.fullmatch(r"market value(?: of (?:the )?land)?|valuation(?: of (?:the )?land)?", text):
        return "VALUATION"
    if re.fullmatch(r"compensation(?: calculation| summary| awarded)?|solatium", text):
        return "COMPENSATION_CALCULATION"
    if re.fullmatch(r"possession(?: of land| reference| proceedings)?", text):
        return "POSSESSION_REFERENCE_OR_SECTION"
    if re.fullmatch(r"(?:court cases?|court proceedings|disputes?|the following cwps were received.*)", text):
        return "COURT_OR_DISPUTE_REFERENCE"
    if re.fullmatch(r"apportionment|entitlement", text):
        return "APPORTIONMENT_OR_ENTITLEMENT"
    if re.fullmatch(r"supplementary(?: award| matter)?", text):
        return "SUPPLEMENTARY_MATTER"
    return None


def _form(heading: str, following: list[dict], semantic: str) -> str:
    lower = heading.casefold()
    if re.search(r"\bannexure\b", lower):
        return "ANNEXURE"
    if re.search(r"\bschedule\b", lower):
        return "SCHEDULE"
    if re.search(r"\btable\b", lower):
        return "TABLE"
    nearby = " ".join(line["text"] for line in following[:4]).casefold()
    if re.search(r"\b(?:s\.?no|rec\.?no)\b", nearby) and re.search(r"\b(?:khasra|claimant|cwp)\b", nearby):
        return "SCHEDULE" if semantic == "LAND_SCHEDULE" else "TABLE"
    if following and len(following[0]["text"]) > 110:
        return "NARRATIVE"
    return "UNKNOWN"


def _source(page: int, line: dict) -> dict:
    return {"page": page, "rawText": " ".join(line["text"].split())[:220],
            "sourceRegion": line.get("sourceRegion")}


def _observation(semantic: str, page: int, line: dict | None, presentation: str = "UNKNOWN",
                 warning: str | None = None) -> dict:
    evidence = [_source(page, line)] if line is not None and line["text"].strip() else []
    return {
        "observationType": "DocumentSection",
        "semantic": semantic,
        "presentation": presentation,
        "pageStart": page,
        "pageEnd": page,
        "rawHeading": " ".join(line["text"].split())[:220] if semantic != "UNKNOWN" and line else None,
        "confidence": 0.85 if semantic != "UNKNOWN" else 0.0,
        "requiresHumanReview": True,
        "evidence": evidence,
        "warnings": [warning or ("Section heading is a reviewable source-context suggestion, not a business fact."
                                  if semantic != "UNKNOWN" else
                                  "No sufficiently clear section heading; source context requires review.")],
        "classifierVersion": CLASSIFIER_VERSION,
    }


def classify_sections(pages: list[dict]) -> list[dict]:
    """Keep multiple/repeated headings separate; extend only explicit continuations."""
    observations: list[dict] = []
    for page in pages:
        number = page["page"]
        lines = page["lines"]
        page_items: list[dict] = []
        for index, line in enumerate(lines):
            raw = " ".join(line["text"].split())
            if not raw:
                continue
            continuation = re.fullmatch(r"(.+?)\s*\(?cont(?:inued|d\.?)(?:\s+on\s+next\s+page)?\)?", raw, re.I)
            clean = continuation.group(1).strip(" .:-") if continuation else raw
            semantic = _heading_semantic(clean)
            standalone_continuation = re.fullmatch(r"cont(?:inued|d\.?)", raw, re.I) is not None
            if continuation or standalone_continuation:
                prior = [item for item in observations if item["pageEnd"] == number - 1]
                match = (prior[-1] if semantic and prior and prior[-1]["semantic"] == semantic else
                         prior[0] if standalone_continuation and len(prior) == 1 and prior[0]["semantic"] != "UNKNOWN" else None)
                if match is not None:
                    match["pageEnd"] = number
                    match["evidence"].append(_source(number, line))
                    match["warnings"].append("Continued on the next page by an explicit source continuation cue.")
                    page_items.append(match)
                    continue
                item = _observation("UNKNOWN", number, line,
                    warning="Continuation cannot be tied to one proven preceding section; review required.")
                observations.append(item)
                page_items.append(item)
                continue
            if semantic is not None:
                item = _observation(semantic, number, line, _form(raw, lines[index + 1:], semantic))
                observations.append(item)
                page_items.append(item)
            elif re.fullmatch(r"claims\s+and\s+compensation|award\s+and\s+possession", raw, re.I):
                item = _observation("UNKNOWN", number, line,
                    warning="Heading has competing section cues; no single section label was assigned.")
                observations.append(item)
                page_items.append(item)
        if not page_items:
            # Even an unreadable page remains a visible review observation.
            first = next((line for line in lines if line["text"].strip()), None)
            observations.append(_observation("UNKNOWN", number, first))
    return observations
