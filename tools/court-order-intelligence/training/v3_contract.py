"""Opt-in V3 parser; frozen legacy and office parser are untouched."""
from pathlib import Path
import sys

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE / 'kaggle'))
sys.path.insert(0, str(HERE.parent))
import json
import re
import jsonschema
from semantics import normalized, SUBMISSION_ROLES
from v3_semantics import validate_fact
from semantic_gate import validate_claims, chronology_context, VERSION
from v3_context import restore_input


def target_schemas_v3():
    """Bound complete chronology to 16 retrieved claims, not eight subclaims.

    No property/type/source/semantic safeguard changes. Legacy schemas are
    copied, never edited. All four V3 comparison modes use this same contract.
    """
    from copy import deepcopy
    from schema.contracts import ANCHOR_SCHEMA, ANSWER_SCHEMA
    result = {'anchors': deepcopy(ANCHOR_SCHEMA), 'claims': deepcopy(ANSWER_SCHEMA)}
    result['claims']['properties']['claims']['maxItems'] = 16
    return result


def parse_v3_output(text, record, schemas, runtime_dir):
    payload = json.loads(text)
    jsonschema.validate(payload, schemas[record['contract']])
    if record['contract'] == 'claims':
        value = restore_input(record['input'])
        validate_claims(payload, value['availableEvidence'], record['task'],
                        intent=value.get('taskIntent'))
    else:
        validate_anchors(payload, record['input'])
    return payload


def validate_anchors(payload, value):
    """One real native window per anchor, never manufactured anchor adjacency."""
    anchors = value['anchors']
    by_id = {a['anchorId']: a for a in anchors}
    if len(by_id) != len(anchors):
        raise ValueError('Duplicate source anchor IDs')
    selections = list(payload['facts'])
    selected = {s['anchorId'] for s in selections}
    for anchor in anchors:
        if anchor['anchorId'] not in selected and anchor.get('speechRole'):
            selections.append({'anchorId': anchor['anchorId'], 'category': anchor['speechRole'],
                               'field': 'context', 'scope': 'Quoted' if anchor.get('quoted') else 'Current'})
    for selection in selections:
        anchor = by_id.get(selection['anchorId'])
        if anchor is None:
            raise ValueError('Unknown source anchor')
        window = value.get('sourceWindows', {}).get(str(anchor['anchorId']))
        if (not window or window['page'] != anchor['page']
                or window.get('orderDate') != value['documentOrderDate']
                or window.get('sha256') != value.get('sourceSha256')
                or not re.fullmatch('[0-9a-f]{64}', window.get('sha256', ''))
                or not 0 < len(window['evidence']) <= 900
                or normalized(anchor['text']) not in normalized(window['evidence'])):
            raise ValueError('Anchor lacks exact bounded native source/page binding')
        if anchor.get('quoted') and selection['scope'] not in ('Quoted', 'Uncertain'):
            raise ValueError('Quoted anchor cannot become current')
        if anchor.get('speechRole') and selection['category'] != anchor['speechRole']:
            raise ValueError('Explicit source speaker attribution must remain')
        if selection['category'] in SUBMISSION_ROLES and not anchor.get('speechRole') and selection['scope'] != 'Uncertain':
            raise ValueError('Party attribution is not independently confirmed')
        actors = anchor['actors']
        subjects = [a for a in actors if re.search(re.escape(a) + r'\s*(?:(?:is|are) directed|shall)\b|'
                    r'\blet\s+(?:the\s+)?' + re.escape(a) + r'\s+(?:file|place|furnish|forward|produce)', anchor['text'], re.I)]
        actor = subjects[0] if len(subjects) == 1 else actors[0] if len(actors) == 1 else None
        deadline = re.search(r'\b(?:(?:preferably\s+)?within|before next|by \d{1,2}(?:st|nd|rd|th)?\b)', anchor['text'], re.I)
        fact = dict(category=selection['category'], field=selection['field'], scope=selection['scope'],
                    value=anchor['text'], evidence=anchor['text'], page=anchor['page'], actor=actor,
                    deadlineText=anchor['text'][deadline.start():].rstrip('.') if deadline else None,
                    targetOrderDate=None, targetActionText=None)
        pages = {window['page']: window['evidence']}
        if anchor.get('evidenceParts'):
            fact['evidenceParts'] = anchor['evidenceParts']
            for part in fact['evidenceParts']:
                part_window = value.get('sourcePartWindows', {}).get(str(part['page']))
                if (not part_window or part_window.get('sha256') != value['sourceSha256']
                        or part_window.get('orderDate') != value['documentOrderDate']
                        or not 0 < len(part_window['evidence']) <= 900):
                    raise ValueError('Missing adjacent part native source window')
                pages[part['page']] = part_window['evidence']
        validate_fact(fact, pages)
        if fact['scope'] == 'Uncertain':
            payload['needsReview'] = True
    # Preserve the legacy opposed-land-assertion review guard across selections.
    for field in ('compensation', 'possession'):
        texts = [by_id[s['anchorId']]['text'].lower() for s in selections if s['field'] == field]
        if (len(texts) > 1 and any(re.search(r'\b(?:not|no|never|unpaid)\b', t) for t in texts)
                and any(not re.search(r'\b(?:not|no|never|unpaid)\b', t) for t in texts)):
            payload['needsReview'] = True


def context_for_v3(input_value):
    """No target access; enrichment copies input and preserves exact source text."""
    from copy import deepcopy
    result = deepcopy(input_value)
    result['semanticGateVersion'] = VERSION
    if 'availableEvidence' in result:
        result['chronologyState'] = chronology_context(result['availableEvidence'])
    return result
