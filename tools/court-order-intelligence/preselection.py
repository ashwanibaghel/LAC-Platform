"""Deterministic evidence candidate selection, never fact extraction.

Input anchors retain their exact IDs, page fragments, quote/speaker metadata and
native adjacent context. Selection cannot override those metadata or any guard.
Omitted evidence remains disclosed; full-document completeness is not claimed.
"""
import json
import re

VERSION = 'high-signal-candidates-v1'
TOPIC = re.compile(r'compensation|possession|reference|affidavit|status report|ownership|title|claim petition|award|khasra|deadline|within|next (?:date|hearing)|renotify|\blist\b', re.I)
OPERATIVE = re.compile(r'is directed|are directed|\bshall\b|\bwe direct\b|Court directs|permitted|may release|to make a reference|be adjudicated|disposed|dismissed|allowed|set aside|renotify|be listed|issue notice|examine.{0,90}documents|process.{0,70}compensation', re.I)
JUDICIAL = re.compile(r'^\s*(?:\d+\.\s*)?(?:Therefore|Accordingly|In view of|In this background|It is now for|We (?:find|hold|note)|The Court)', re.I)
LAC_POSITION = re.compile(r'(?:stand|contention|affidavit|reply|submission).{0,65}(?:LAC|respondent)|(?:LAC|respondent).{0,65}(?:stand|contention|affidavit|reply|submission)', re.I)
HISTORY = re.compile(r'earlier|previous|prior order|historical|as per the above order|vide order dated', re.I)
PRAYER = re.compile(r'petition.{0,180}(?:seek|pray|relief)|(?:seek|pray).{0,100}(?:direction|compensation|relief)', re.I)


def select_candidates(anchors, pages, budget=20):
    """Leave size headroom for <=4 calls; NEVER discard overflow critical anchors.

    No case identity, date, gold label, expected answer or judge is an input.
    Critical current operative anchors survive even if the budget is exceeded;
    the existing worker's bounded coverage limit still fails closed if needed.
    """
    if not isinstance(budget, int) or budget < 6:
        raise ValueError('Candidate budget must allow at least one complete chunk')
    final_pages = set(sorted(pages)[-2:])
    ranked = []
    critical = set()
    for anchor in anchors:
        text = anchor['text']
        current = not anchor.get('quoted')
        operative = bool(OPERATIVE.search(text))
        topic = bool(TOPIC.search(text))
        judicial = bool(JUDICIAL.search(text))
        tail = anchor['page'] in final_pages
        lac = bool(LAC_POSITION.search(text))
        speech = bool(anchor.get('speechRole'))
        prayer = current and bool(PRAYER.search(text))
        petitioner_position = current and anchor.get('speechRole') == 'PETITIONER_SUBMISSION' and topic
        # These are lexical evidence-selection reasons, NOT semantic labels.
        reasons = [name for name, yes in (
            ('operative-language', operative), ('case-topic', topic),
            ('final-pages', tail), ('judicial-narrative', judicial),
            ('lac-respondent-position', lac), ('source-speaker', speech),
            ('petition-relief', prayer), ('petitioner-position', petitioner_position)) if yes]
        if prayer or petitioner_position or current and not speech and (operative or judicial and topic):
            critical.add(anchor['anchorId'])
        score = (40*operative + 20*topic + 40*tail + 15*judicial +
                 30*lac + 10*speech + 30*current - 20*bool(HISTORY.search(text)))
        ranked.append((score, anchor['anchorId'], reasons, anchor))
    picked = set(critical)
    # Retain a small source-ordered sample of earlier operative/topic evidence
    # for historical contrast. Never remove its original quoted flag/context.
    history_context = [a for a in anchors if a.get('quoted') and not a.get('speechRole')
                       and TOPIC.search(a['text']) and re.search(r'\bLAC\b|Collector', a['text'], re.I)]
    if len(anchors) > budget and len(critical) <= budget-2:
        picked.update(a['anchorId'] for a in history_context[:2])
    for _, anchor_id, _, _ in sorted(ranked, key=lambda row: (-row[0], row[1])):
        if len(picked) >= max(budget, len(critical)):
            break
        picked.add(anchor_id)
    # Retain source ordering, do not concatenate distant selected passages.
    selected = [anchor for anchor in anchors if anchor['anchorId'] in picked]
    audit = dict(policy=VERSION, originalAnchorCount=len(anchors),
                 selectedAnchorCount=len(selected), omittedAnchorCount=len(anchors)-len(selected),
                 selectedAnchorIds=[a['anchorId'] for a in selected],
                 omittedAnchorIds=[a['anchorId'] for a in anchors if a['anchorId'] not in picked],
                 selectionReasons={str(aid): reasons for _, aid, reasons, _ in ranked if aid in picked},
                 wholeDocumentCoverage=False if len(selected) < len(anchors) else True,
                 criticalBudgetOverflow=len(critical) > budget)
    return selected, audit


def bounded_chunks(anchors):
    """Same six-anchor and 5000-character evidence bounds as existing extractor."""
    chunks, current, length = [], [], 0
    for anchor in anchors:
        size = len(json.dumps(anchor))
        if current and (length+size > 5000 or len(current) >= 6):
            chunks.append(current)
            current, length = [], 0
        current.append(anchor)
        length += size
    if current:
        chunks.append(current)
    return chunks
