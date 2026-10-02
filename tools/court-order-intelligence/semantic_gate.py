"""V3 opt-in task relevance gate AFTER the unchanged structural/source parser.

No network, model, expected answers, case-specific rules or persistent state.
This gate does not prove retrieval recall, and is not enabled in office runtime.
Input facts/context must first pass the existing source/page/role validation.
Unknown lifecycle remains unknown: silence/expiry never proves completion.
"""
import re

from semantics import dates_in, normalized, party_speech, IMPERATIVE, OFFICE

VERSION = 'court-task-semantics-v3.1'
PERFORMANCE = re.compile(
    r'\b(?:has|have|had|was|were)\s+(?:been\s+)?(?:filed|forwarded|deposited|paid|'
    r'produced|submitted|completed)|\btaken on record\b|\bcomplied with\b', re.I)
NOT_PERFORMED = re.compile(
    r'\b(?:not|never|yet to|no compliance|shall|will|would|directed to|to be filed|'
    r'time (?:is |was )?granted)\b', re.I)
LISTING = re.compile(r'\blist(?:ed)?\b|\brenotify\b|\badjourn\b|next (?:hearing|date)', re.I)


def chronology_context(entries):
    """Expose existing metadata without manufacturing actors or chain lifecycle.

    `Current` means current in the source order, NOT still active in the chain.
    Chain lifecycle is supplied only by independently source-reviewed metadata.
    Missing lifecycle/meaningful-development knowledge is explicitly UNKNOWN.
    The calling V3 exporter must bind additional actor context to source pages.
    """
    dates = [entry['source']['orderDate'] for entry in entries]
    latest = max(dates, default=None)
    return [{'factId': entry['factId'], 'orderDate': entry['source']['orderDate'],
             'role': entry['category'], 'field': entry.get('field'),
             'actor': entry.get('actor'), 'scope': entry['scope'],
             'directionLifecycle': entry.get('directionLifecycle', 'UNKNOWN'),
             'relatedEvidenceIds': entry.get('relatedEvidenceIds', []),
             'procedural': entry['category'] == 'PROCEDURAL_EVENT',
             'latestSuppliedOrder': entry['source']['orderDate'] == latest,
             'latestMeaningfulDevelopment': entry.get('latestMeaningfulDevelopment', 'UNKNOWN')}
            for entry in entries]


def _source(entry):
    source = entry['source']
    if not source.get('orderDate') or not isinstance(source.get('page'), int) or source['page'] < 1:
        raise ValueError('Semantic gate: missing dated page evidence')
    evidence = normalized(source['evidence'])
    if not evidence or normalized(entry['text']) not in evidence:
        raise ValueError('Semantic gate: selected text is not in its cited evidence')
    if entry['scope'] == 'Uncertain':
        raise ValueError('Semantic gate: uncertain evidence cannot establish an answer')
    return evidence


def validate_claims(payload, entries, task, *, intent=None):
    """Reject an entire unsafe selection, never replace it with gold/empty targets.

    Attribution for anchors remains checked by anchors.expand, independently of
    this claims gate. Compliance completion cannot be established by a direction.
    All systems must receive this SAME gate in V3 comparisons; report its catches
    separately from weight improvements and from frozen legacy continuity scores.
    """
    if [entry.get('factId') for entry in entries] != list(range(len(entries))):
        raise ValueError('Semantic gate: noncontiguous retrieved evidence IDs')
    for claim in payload['claims']:
        index = claim['factId']
        if isinstance(index, bool) or not isinstance(index, int) or not 0 <= index < len(entries):
            raise ValueError('Semantic gate: unretrieved evidence ID')
        entry = entries[index]
        evidence = _source(entry)
        role, scope = entry['category'], entry['scope']
        if task == 'compliance_state':
            # Existing claims contract answers proof of performance, not an
            # unconstrained generated OPEN/COMPLETED state. Other state queries
            # need an explicit reviewed contract before training can launch.
            if (role not in ('RECORDED_COMPLIANCE', 'COURT_FINDING', 'PROCEDURAL_EVENT')
                    or entry.get('field') != 'compliance' or scope != 'Current'
                    or party_speech(evidence) or not PERFORMANCE.search(evidence)
                    or NOT_PERFORMED.search(evidence)):
                raise ValueError('Semantic gate: completion requires recorded performance')
        elif task == 'office_action_detection':
            actor = entry.get('actor') or ''
            if (role != 'COURT_DIRECTION' or scope != 'Current'
                    or entry.get('field') != 'direction' or not OFFICE.search(actor)
                    or re.search(r'DDA|petitioner|Faridabad|Haryana', actor, re.I)
                    or normalized(actor) not in evidence
                    or party_speech(evidence) or not IMPERATIVE.search(evidence)
                    or entry.get('directionLifecycle') not in ('OPEN', 'PARTIAL')):
                raise ValueError('Semantic gate: action requires supported active LAC direction')
        elif task == 'date_specific_retrieval_or_QA' and intent == 'next_hearing':
            if (entry.get('field') != 'nextHearing' or scope != 'Current'
                    or role not in ('COURT_DIRECTION', 'NEXT_HEARING', 'PROCEDURAL_EVENT')
                    or party_speech(evidence) or not LISTING.search(evidence)
                    or not dates_in(entry['text'])
                    or entry.get('dateLifecycle') != 'LATEST_CONFIRMED'):
                raise ValueError('Semantic gate: next date requires current source-backed listing')
        elif task == 'multi_order_current_position':
            if scope == 'Quoted':
                raise ValueError('Semantic gate: quoted evidence is not current state')
            if role == 'COURT_DIRECTION' and entry.get('directionLifecycle') not in ('OPEN', 'PARTIAL'):
                raise ValueError('Semantic gate: stale/unknown direction cannot establish current action')
    return payload
