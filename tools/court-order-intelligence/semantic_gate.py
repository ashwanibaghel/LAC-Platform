"""V3 opt-in task relevance gate AFTER the unchanged structural/source parser.

No network, model, expected answers, case-specific rules or persistent state.
This gate does not prove retrieval recall, and is not enabled in office runtime.
Input facts/context must first pass the existing source/page/role validation.
Unknown lifecycle remains unknown: silence/expiry never proves completion.
"""
import re

from semantics import dates_in, normalized, OFFICE
from v3_semantics import party_speech, OPERATIVE, actor_supported, judicial_context_safe

VERSION = 'court-task-semantics-v3.2'
PERFORMANCE = re.compile(
    r'\b(?:has|have|had|was|were)\s+(?:now\s+|already\s+)?(?:been\s+)?(?:filed|forwarded|deposited|paid|'
    r'produced|submitted|completed|served)\b|\bhave placed before the Court\b|'
    r'\bstood served\b|\btaken on record\b|\bcomplied with\b', re.I)
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
    for context in entry.get('sourceContext', []):
        if (not isinstance(context.get('page'), int) or context['page'] < 1
                or not 0 < len(context.get('evidence', '')) <= 900
                or not re.fullmatch('[0-9a-f]{64}', context.get('sha256', ''))
                or context.get('sha256') != source.get('sha256')
                or context.get('officialUrl') != source['officialUrl']
                or context.get('orderDate') != source['orderDate']):
            raise ValueError('Semantic gate: adjacent context source binding failed')
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
        if role in ('COURT_FINDING', 'COURT_OBSERVATION', 'COURT_DIRECTION', 'RECORDED_COMPLIANCE'):
            if party_speech(entry['text']) or not judicial_context_safe(entry['text'], evidence):
                raise ValueError('Semantic gate: party assertion cannot become judicial fact')
        if task == 'compliance_state':
            # Existing claims contract answers proof of performance, not an
            # unconstrained generated OPEN/COMPLETED state. Other state queries
            # need an explicit reviewed contract before training can launch.
            if (role not in ('RECORDED_COMPLIANCE', 'COURT_FINDING', 'PROCEDURAL_EVENT')
                    or entry.get('field') != 'compliance' or scope != 'Current'
                    or party_speech(entry['text']) or not PERFORMANCE.search(entry['text'])
                    or NOT_PERFORMED.search(entry['text']) or not actor_supported(entry.get('actor'), entry)
                    or not re.search(r'\b(?:compliance|in terms of|pursuant to|directed|undertakes?|obligation)\b',
                        ' '.join([evidence, *(c['evidence'] for c in entry.get('sourceContext', []))]), re.I)):
                raise ValueError('Semantic gate: completion requires recorded performance')
        elif task == 'office_action_detection':
            actor = entry.get('actor') or ''
            if (role != 'COURT_DIRECTION' or scope != 'Current'
                    or entry.get('field') not in ('direction', 'filing', 'documents', 'referenceToAdj', 'compensation', 'possession') or not OFFICE.search(actor)
                    or re.search(r'DDA|petitioner|Faridabad|Haryana', actor, re.I)
                    or not actor_supported(actor, entry)
                    or party_speech(entry['text']) or not OPERATIVE.search(entry['text'])
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
