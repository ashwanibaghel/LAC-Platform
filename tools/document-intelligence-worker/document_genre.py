"""Conservative, source-only document genre routing for the V2 Award worker."""
from __future__ import annotations

import re

CLASSIFIER_VERSION = "document-genre-rules/1.0"
AWARD = "AWARD"
SUPPLEMENTARY_AWARD = "SUPPLEMENTARY_AWARD"
POSSESSION_PROCEEDINGS = "POSSESSION_PROCEEDINGS"
CLAIMANT_REGISTER_OR_CONTINUATION = "CLAIMANT_REGISTER_OR_CONTINUATION"
RENTAL_OR_REQUISITION_OFFER = "RENTAL_OR_REQUISITION_OFFER"
UNKNOWN = "UNKNOWN"


def should_extract_award(genre: str) -> bool:
    return genre in (AWARD, SUPPLEMENTARY_AWARD)


def _genre_cues(text: str) -> set[str]:
    normalized = " ".join(text.casefold().split())
    matches: set[str] = set()
    if re.search(r"\b(?:supplementary|supplemental)\s+award\b", normalized):
        matches.add(SUPPLEMENTARY_AWARD)
    if re.search(r"\bkabza\s+kar(?:w|v)a?y?i\b|\bpossession\s+proceedings\b|\bproceedings\s+(?:for|of)\s+(?:taking|delivery|handing)\s+(?:over\s+)?possession\b", normalized):
        matches.add(POSSESSION_PROCEEDINGS)
    if (re.search(r"\b(?:annual\s+)?rental\s+offer\b", normalized) or
            re.search(r"\b(?:offer|proposal)\s+(?:of|for)\s+(?:annual\s+)?rent\b", normalized)):
        matches.add(RENTAL_OR_REQUISITION_OFFER)
    if re.search(r"\b(?:claimant|claimants)\s+(?:register|list|schedule|continuation)\b|\b(?:register|list|schedule)\s+of\s+claimants\b", normalized):
        matches.add(CLAIMANT_REGISTER_OR_CONTINUATION)
    return matches


def classify_document(pages: list[dict]) -> dict:
    """Use OCR title/intro lines across pages; context such as filename is never read."""
    evidence: dict[str, list[dict]] = {}
    award_headers: list[tuple[dict, int]] = []
    award_context: list[dict] = []
    first_page_non_award_titles: list[int] = []
    for page in pages:
        page_number = page["page"]
        # Titles and introductory text can move down scanned pages. A long
        # body mention alone must not decide a document's genre.
        for index, line in enumerate(page["lines"][:20]):
            raw = " ".join(line["text"].split())
            if not raw or len(raw) > 220:
                continue
            item = {"page": page_number, "rawText": raw, "sourceRegion": line.get("sourceRegion")}
            if index < 12:
                for genre in _genre_cues(raw):
                    evidence.setdefault(genre, []).append(item)
                    if page_number == pages[0]["page"]:
                        first_page_non_award_titles.append(index)
                if re.match(r"^(?:the\s+)?award\s*(?:no\.?|number|under\s+section|u/s)\b", raw, re.I):
                    award_headers.append((item, index))
            if page_number == pages[0]["page"] and re.search(r"\b(?:land acquisition act|section\s+11\s+of\s+the\s+act|acquisition of land)\b", raw, re.I):
                award_context.append(item)

    # A supplementary title is more specific than an Award number printed in
    # the same document. Other competing genre titles are never voted away.
    if award_headers and award_context and SUPPLEMENTARY_AWARD not in evidence and not any(
            index < award_headers[0][1] for index in first_page_non_award_titles):
        evidence[AWARD] = [award_headers[0][0], award_context[0]]
    active = [genre for genre, cues in evidence.items() if cues]
    if len(active) == 1:
        genre = active[0]
        cues = evidence[genre][:8]
        confidence = 0.9 if genre != AWARD else 0.85
        warnings = ["Genre is a source-backed routing suggestion; human review is required."]
    else:
        genre = UNKNOWN
        groups = [group for group in evidence.values() if group]
        # Keep at least one cue from each competing genre before truncation.
        cues = ([group[0] for group in groups] + [cue for group in groups for cue in group[1:]])[:8]
        confidence = 0.0
        warnings = ["Conflicting genre headings require human review." if active else
                    "No sufficiently clear document-genre heading was found; human review is required."]
    return {
        "observationType": "DocumentGenre",
        "genre": genre,
        "confidence": confidence,
        "requiresHumanReview": True,
        "page": cues[0]["page"] if cues else pages[0]["page"],
        "evidence": cues,
        "warnings": warnings,
        "classifierVersion": CLASSIFIER_VERSION,
    }
