"""Deterministic experimental concepts. Numeric values are intentionally never repaired."""
from __future__ import annotations

import re
from dataclasses import dataclass

from .normalized import DocumentPage

KHASRA = re.compile(r"\b[1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*(?:\s+min)?\b", re.I)
AREA = re.compile(r"\b\d{1,4}\s*(?:-|–|—)\s*\d{1,2}\b")
CWP = re.compile(r"\b(?:CWP|W\.?P\.?)\s*(?:No\.?\s*)?\d{1,6}\s*/\s*\d{4}\b", re.I)


@dataclass(frozen=True)
class Candidate:
    concept: str
    value: str
    source_page: int
    source_text: str
    classification: str = "NeedsHumanInterpretation"


def interpret(pages: list[DocumentPage]) -> list[Candidate]:
    """Find safe concept candidates; do not create domain entities or infer legal effect."""
    candidates: list[Candidate] = []
    for page in pages:
        text = "\n".join(block.text for block in page.blocks)
        lower = text.lower()
        for match in CWP.finditer(text):
            candidates.append(Candidate("CourtCaseNumber", match.group(), page.page_number, text))
        if "classification of land" in lower or "b block" in lower or "a block" in lower:
            for match in KHASRA.finditer(text):
                candidates.append(Candidate("LandClassificationKhasra", match.group(), page.page_number, text))
        if "claim and evidence" in lower or "name of claimant" in lower:
            for match in KHASRA.finditer(text):
                candidates.append(Candidate("ClaimKhasra", match.group(), page.page_number, text))
        if "khasra" in lower and ("area awarded" in lower or "total area" in lower):
            for match in KHASRA.finditer(text):
                candidates.append(Candidate("AwardKhasra", match.group(), page.page_number, text))
            for match in AREA.finditer(text):
                candidates.append(Candidate("AwardAreaUnassigned", match.group(), page.page_number, text))
    return candidates
