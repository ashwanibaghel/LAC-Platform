"""Opt-in V3 source semantics. Never imported by office/legacy runtime.

No case IDs, expected answers, target lookup, model or network. Generic language
precision only; a judicial modal never cancels an actual reported allegation.
"""
import re
import jsonschema
from semantics import normalized, SCHEMA, ROLE_FIELDS, SUBMISSION_ROLES, OFFICE, dates_in

JUDICIAL = ('COURT_DIRECTION', 'COURT_FINDING', 'COURT_OBSERVATION', 'RECORDED_COMPLIANCE')
OPERATIVE = re.compile(
    r'\bshall\b|\b(?:is|are) directed\b|\b(?:we|Court) directs?\b|\bit is directed\b|'
    r'\blet\b.{0,180}\b(?:filed|file|placed|place|submitted|submit|impleaded|brought|furnish)\b|'
    r'\b(?:be filed|be served|be completed|be placed on record|be brought on record)\b|'
    r'\b(?:last|final) (?:and final )?opportunity\b.{0,180}\b(?:file|bring|argue)\b|'
    r'\b(?:opportunity|permission|time) (?:is |was )?granted\b.{0,160}\b(?:file|argue)\b|'
    r'\blist (?:the matter |matter )?(?:on|for|before)\b|\bre-?notify\b|\bissue notice\b', re.I)
SPEECH = re.compile(
    r'\b(?:submits?|submitted|contends?|contended|alleges?|alleged|claims|claimed|'
    r'asserts?|asserted|argues?|argued|prays?|seeks?|averred|undertakes?|according to|'
    r'pointed out|it is stated|are stated|is stated|made the following submissions|submission of)\b', re.I)
COURT_NARRATIVE = re.compile(
    r'\brecord (?:has been|was) perused\b|\b(?:Court|we)\b.{0,100}\b(?:perused|find|hold|observe|note)\b|'
    r'\b(?:delay|delay in filing)\b.{0,130}\bcondoned\b|\bprayer\b.{0,100}\bnot made out\b|'
    r'\bis (?:set aside|allowed|dismissed)\b|\b(?:this )?Court is of the opinion\b', re.I)


def party_speech(text):
    value = normalized(text)
    # Only the infinitive immediately governed by a judicial modal/opportunity
    # is exempt. A preceding "counsel submits that" remains a speech marker.
    value = re.sub(r'\bshall\s+(?:also\s+)?(?:submit|seek|argue)\b', 'shall perform', value, flags=re.I)
    # A passive filing instruction is not counsel's past-tense submission.
    # Exempt only this governed verb, leaving any actual speech marker intact.
    value = re.sub(r'\bbe submitted\b', 'be performed', value, flags=re.I)
    if re.search(r'\b(?:opportunity|permission)\b.{0,140}\b(?:to|and) argue\b', value, re.I):
        value = re.sub(r'\b(?:to|and) argue\b', 'to perform', value, flags=re.I)
    if SPEECH.search(value) or re.search(
            r'\b(?:counsel|petitioner|respondent|LAC|Mr\.?|Ms\.?)\b[^.!?]{0,120}'
            r'\b(?:states?|stated|reports? that|reported that)\b|'
            r'\b(?:case|stand|contention) of the (?:petitioners?|respondents?|LAC) is that\b', value, re.I):
        return True
    # A bare affidavit reference is still insufficient to authorize its content
    # as a judicial fact. Explicit Court perusal/operative/condonation wording
    # distinguishes a requirement or procedural decision from an affidavit claim.
    if re.search(r'counter.affidavits?', value, re.I):
        if not (OPERATIVE.search(value) or COURT_NARRATIVE.search(value)):
            return True
    if re.search(r'\bprayer\b', value, re.I) and not COURT_NARRATIVE.search(value):
        return True
    return False


def judicial_context_safe(selected, native):
    """Use actual adjacency, never unrelated speech elsewhere on the page.

    A fresh numbered operative paragraph after a closed sentence resets the
    voice. A colon introducing a party's requirements/quotation does not.
    Explicit quotation/scope metadata is checked separately by the contract.
    """
    selected, native = normalized(selected), normalized(native)
    start = native.index(selected)
    prefix = native[max(0, start - 240):start]
    if not party_speech(prefix):
        return True
    if prefix.rstrip().endswith(':'):
        return False
    fresh = re.match(r'^(?:\d+\.\s*)?(?:(?:Accordingly,\s*)?let\b|re-?notify|'
                     r'list (?:the matter |matter )?(?:on|for|before)|issue notice)', selected, re.I)
    reset = re.search(r'(?:Court|we) (?:direct|order|hold|observe|find|note)|it is directed|'
                      r'\b(?:matter|issue) is referred\b', prefix, re.I)
    numbered = re.match(r'^\d+\.\s+', selected) and prefix.rstrip().endswith('.') and OPERATIVE.search(selected)
    stated_judicial_reset = prefix.rstrip().endswith('.') and re.search(
        r'\b(?:this )?Court (?:is of the opinion|finds?|holds?|observes?|notes?|directs?)\b', selected, re.I)
    return bool(fresh or reset or numbered or stated_judicial_reset)


def actor_supported(actor, entry):
    if not actor:
        return False
    parts = [entry['source']['evidence'], *(c['evidence'] for c in entry.get('sourceContext', []))]
    text = normalized(' '.join(parts))
    actors = [a.strip() for a in actor.split('/')]
    for name in actors:
        if re.search(r'(?<!\w)' + re.escape(normalized(name)) + r'(?!\w)', text, re.I):
            continue
        # Explicit official title alias only, not generic "respondents". Native
        # orders also use this spelling of the named title; no fuzzy identities.
        if name == 'LAC' and re.search(r'\bLand Acqu[i]?sition Collector\b', text, re.I):
            continue
        return False
    return True


def validate_fact(fact, pages):
    """Keep source/page/value/role/qualifier guards with precise speech check."""
    jsonschema.validate({'facts': [fact], 'needsReview': False}, SCHEMA)
    evidence = normalized(fact['evidence'])
    parts = fact.get('evidenceParts')
    if parts:
        if (parts[0]['page'] != fact['page'] or parts[1]['page'] != parts[0]['page'] + 1
                or normalized(' '.join(p['evidence'] for p in parts)) != evidence
                or any(p['page'] not in pages or normalized(p['evidence']) not in normalized(pages[p['page']]) for p in parts)):
            raise ValueError('Cross-page evidence is not on exact adjacent source pages')
    elif fact['page'] not in pages or evidence not in normalized(pages[fact['page']]):
        raise ValueError('Evidence is not on cited supplied source page')
    if normalized(fact['value']) not in evidence:
        raise ValueError('Fact value must remain verbatim in cited evidence')
    if fact['category'] in ROLE_FIELDS and fact['field'] != ROLE_FIELDS[fact['category']]:
        raise ValueError('Semantic role does not match structured field')
    if fact['category'] not in SUBMISSION_ROLES and party_speech(evidence):
        raise ValueError('Party submission must retain party attribution')
    for key in ('actor', 'deadlineText'):
        if fact[key] and normalized(fact[key]) not in evidence:
            raise ValueError(key + ' is not supported by cited evidence')
    if fact['field'] in ('direction', 'finding', 'compensation', 'possession', 'compliance'):
        for marker in (r'\bnot\b', r'\bno\b', r'\bnever\b', r'\bif any\b', r'\bpreferably\b'):
            if re.search(marker, evidence, re.I) and not re.search(marker, fact['value'], re.I):
                raise ValueError('Fact omits source negation or material condition')
    patterns = {'DEADLINE': r'\bwithin\b|\bbefore\b|\bby\s+\d',
                'DISPOSITION': r'set aside|allowed|dismiss|disposed|disposal|closed',
                'NEXT_HEARING': r'\blist\b|be listed|re-?notify|next (?:date|hearing)|adjourn'}
    if fact['category'] in patterns and not re.search(patterns[fact['category']], evidence, re.I):
        raise ValueError('Semantic role lacks explicit source support')
    if fact['category'] in JUDICIAL:
        if fact['category'] == 'COURT_DIRECTION' and not OPERATIVE.search(evidence):
            raise ValueError('Nonoperative text cannot authorize a Court direction')
        if not judicial_context_safe(parts[0]['evidence'] if parts else evidence, pages[fact['page']]):
            raise ValueError('Direction excerpt drops nearby party attribution')
    if fact['field'] in ('compliance', 'supersession'):
        if fact['category'] not in ('COURT_FINDING', 'PROCEDURAL_EVENT', 'RECORDED_COMPLIANCE') or party_speech(evidence):
            raise ValueError('Party claim cannot close an office direction')
        if fact['field'] == 'compliance' and not re.search(
                r'\b(?:complied|compliance|direction|directed|in terms of)\b', evidence, re.I):
            raise ValueError('Compliance requires a source-linked obligation')
        if fact['field'] == 'compliance' and not re.search(
                r'\b(?:filed|complied|completed|done|placed on record|placed before the Court|compliance|served)\b', evidence, re.I):
            raise ValueError('Compliance requires recorded performance')
