"""Independent validation and conservative, source-linked chronological synthesis."""
import calendar
import hashlib
import re
from datetime import date, timedelta
import jsonschema

VERSION = 'court-native-v1-anchors'
CATEGORIES = ['COURT_DIRECTION', 'COURT_FINDING', 'LAC_OR_RESPONDENT_SUBMISSION',
              'PETITIONER_SUBMISSION', 'PROCEDURAL_EVENT', 'HISTORICAL_LAND_FACT']
FIELDS = ['direction', 'finding', 'compliance', 'supersession', 'village', 'khasra',
          'award', 'compensation', 'possession', 'section18', 'section30_31',
          'referenceToAdj', 'acquisitionSection', 'filing', 'documents', 'bench', 'nextHearing']
NULL_STRING = {'type': ['string', 'null'], 'maxLength': 900}
FACT = {'type': 'object', 'additionalProperties': False,
        'properties': {'category': {'type': 'string', 'enum': CATEGORIES},
                       'field': {'type': 'string', 'enum': FIELDS},
                       'value': {'type': 'string', 'minLength': 1, 'maxLength': 900},
                       'actor': NULL_STRING, 'deadlineText': NULL_STRING,
                       'scope': {'type': 'string', 'enum': ['Current', 'Historical', 'Quoted', 'Uncertain']},
                       'page': {'type': 'integer', 'minimum': 1},
                       'evidence': {'type': 'string', 'minLength': 8, 'maxLength': 900},
                       'targetOrderDate': NULL_STRING, 'targetActionText': NULL_STRING},
        'required': ['category', 'field', 'value', 'actor', 'deadlineText', 'scope',
                     'page', 'evidence', 'targetOrderDate', 'targetActionText']}
SCHEMA = {'type': 'object', 'additionalProperties': False,
          'properties': {'facts': {'type': 'array', 'items': FACT, 'maxItems': 6},
                         'needsReview': {'type': 'boolean'}}, 'required': ['facts', 'needsReview']}

def normalized(text):
    return re.sub(r'\s+', ' ', text).strip()

def identity(text):
    return re.sub('[^a-z0-9]', '', text.lower())

def dates_in(text):
    values = set()
    for day, month, year in re.findall(r'\b(\d{1,2})[./-](\d{1,2})[./-](20\d\d|19\d\d)\b', text):
        try:
            values.add(date(int(year), int(month), int(day)).isoformat())
        except ValueError:
            pass
    names = '|'.join(calendar.month_name[1:])
    for day, month, year in re.findall(r'\b(\d{1,2})(?:st|nd|rd|th)?\s+(' + names + r'),?\s+(\d{4})', text, re.I):
        values.add(date(int(year), next(i for i, name in enumerate(calendar.month_name) if name.lower() == month.lower()), int(day)).isoformat())
    for month, day, year in re.findall(r'\b(' + names + r')\s+(\d{1,2}),?\s+(\d{4})', text, re.I):
        values.add(date(int(year), next(i for i, name in enumerate(calendar.month_name) if name.lower() == month.lower()), int(day)).isoformat())
    return values

def due_date(order_date, deadline, next_hearing=None):
    if not deadline:
        return None
    text = deadline.lower()
    if 'before' in text and re.search(r'next (?:date|hearing)', text):
        return next_hearing
    explicit = dates_in(text)
    if len(explicit) == 1 and not re.search(r'receipt|service|thereafter|determination', text):
        return next(iter(explicit))
    if not re.search(r'from (?:today|the date of (?:this|the) (?:order|judgment)|this (?:order|judgment))', text):
        return None
    match = re.search(r'within (?:a period of )?(\d+|one|two|three|four|five|six|seven|eight|nine|ten|twelve)\s*(?:\(\d+\)\s*)?(days?|weeks?|months?)', text)
    if not match or not order_date:
        return None
    numbers = dict(zip('one two three four five six seven eight nine ten twelve'.split(), [1,2,3,4,5,6,7,8,9,10,12]))
    amount = int(match[1]) if match[1].isdigit() else numbers[match[1]]
    start = date.fromisoformat(order_date)
    if match[2].startswith('month'):
        index = start.year * 12 + start.month - 1 + amount
        year, month0 = divmod(index, 12)
        return date(year, month0+1, min(start.day, calendar.monthrange(year, month0+1)[1])).isoformat()
    return (start + timedelta(days=amount * (7 if match[2].startswith('week') else 1))).isoformat()

SUBMISSION = re.compile(r'\b(?:submits?|submitted|contends?|alleges?|claims|claimed|asserts?|argues?|prays?|seeks?|averred|undertakes?|prayer|counter.affidavit|according to)\b', re.I)
IMPERATIVE = re.compile(r'\b(?:is directed|are directed|shall|let .*?(?:file|place|furnish)|it is directed|be filed|be listed|list (?:on|for|before)|renotify|issue notice|we direct|Court directs)\b', re.I)
OFFICE = re.compile(r'\b(?:LAC|Land Acquisition Collector|ADM[/ -]LAC|Collector)\b', re.I)

def party_speech(text):
    return bool(SUBMISSION.search(text) or re.search(
        r'\b(?:counsel|petitioner|respondent|LAC|Mr\.?|Ms\.?)\b[^.!?]{0,120}\b(?:states?|stated|reports?\s+that|reported\s+that)\b',text,re.I))

def validate(payload, pages):
    jsonschema.validate(payload, SCHEMA)
    for fact in payload['facts']:
        evidence = normalized(fact['evidence'])
        if fact['page'] not in pages or evidence not in normalized(pages[fact['page']]):
            raise ValueError('Evidence is not on cited supplied source page')
        if normalized(fact['value']) not in evidence:
            raise ValueError('Fact value must be a verbatim part of its evidence')
        if fact['field'] in ('direction', 'finding', 'compensation', 'possession', 'compliance'):
            for marker in (r'\bnot\b', r'\bno\b', r'\bnever\b', r'\bif any\b', r'\bpreferably\b'):
                if re.search(marker, evidence, re.I) and not re.search(marker, fact['value'], re.I):
                    raise ValueError('Fact omits a source negation or material condition')
        for field in ('actor', 'deadlineText'):
            if fact[field] and normalized(fact[field]) not in evidence:
                raise ValueError(field + ' is not supported by cited evidence')
        if fact['category'] in ('COURT_DIRECTION','COURT_FINDING'):
            if party_speech(evidence):
                raise ValueError('Party submission cannot become a Court finding or direction')
            if fact['category']=='COURT_DIRECTION' and not IMPERATIVE.search(evidence):
                raise ValueError('Submission or nonoperative text cannot authorize a Court direction')
            page = normalized(pages[fact['page']])
            start = page.index(evidence)
            prefix = page[max(0,start-240):start]
            # An operative setting-aside/allowing decision can mention the
            # petitioner's "prayer" without being a party submission. Do not
            # drop genuine speech verbs; this reset is judicial, not a claim.
            if re.search(r'\bis (?:set aside|allowed|dismissed)\b',prefix,re.I) and not re.search(r'\b(?:submits?|submitted|contends?|claims|claimed|alleges?|asserts?|argues?|prays?|seeks?|states?|stated)\b',prefix,re.I):
                prefix=re.sub(r'\bprayer\b','',prefix,flags=re.I)
            fresh_procedure=re.match(r'^(?:\d+\.\s*)?(?:renotify|list (?:on|for)|issue notice)\b',evidence,re.I)
            if party_speech(prefix) and not fresh_procedure and not re.search(r'(?:Court|we) (?:direct|order|hold|observe|find|note)|it is directed', prefix, re.I):
                raise ValueError('Direction excerpt drops nearby party-submission attribution')
        if fact['field'] in ('compliance', 'supersession'):
            if fact['category'] not in ('COURT_FINDING', 'PROCEDURAL_EVENT') or party_speech(evidence):
                raise ValueError('Party claim cannot close an office direction')
            if fact['field'] == 'compliance' and not (re.search(r'\b(?:complied|compliance|direction|directed)\b', evidence, re.I) and re.search(r'\b(?:filed|complied|completed|done|placed on record|compliance)\b', evidence, re.I)):
                raise ValueError('Compliance requires an explicit obligation and recorded performance')
        if fact['scope'] == 'Uncertain':
            payload['needsReview'] = True
    # Opposed assertions on the same land dimension are review work, never a chosen fact.
    for field in ('compensation', 'possession'):
        facts = [f['value'].lower() for f in payload['facts'] if f['field'] == field]
        if len(facts) > 1 and any(re.search(r'\b(?:not|no|never|unpaid)\b', x) for x in facts) and any(not re.search(r'\b(?:not|no|never|unpaid)\b', x) for x in facts):
            payload['needsReview'] = True
    return payload

def office_action(fact, order):
    if (fact['category'] != 'COURT_DIRECTION' or fact['scope'] != 'Current'
            or not fact['actor'] or not OFFICE.search(fact['actor'])
            or re.search(r'Faridabad|Haryana|DDA|petitioner', fact['actor'], re.I)
            or re.search(r'Faridabad|Haryana', fact['evidence'], re.I)):
        return None
    evidence = fact['evidence']
    actor = re.escape(fact['actor'])
    if not re.search(actor + r'\s*(?:(?:is|are)\s+directed|shall)\b|\blet\s+(?:the\s+)?' + actor + r'\s+(?:file|place|furnish|forward|produce)', evidence, re.I):
        return None
    # Quoted/historical passage context is rejected even when a model calls it current.
    if re.search(r'(?:order|judgment) dated|quoted|reads as|as under|Supreme Court|[“”]', evidence, re.I):
        return None
    text = fact['value']
    action_type = next((label for label, pattern in [
        ('Reference', r'reference|ADJ|District Judge'), ('Filing', r'affidavit|status report|reply'),
        ('Compensation', r'compensation|deposit|release|payment'),
        ('Documents', r'award|notification|document|record'),
        ('Appearance/record', r'appear|produce'), ('LAC proceeding', r'hearing|objection|decide')]
        if re.search(pattern, text, re.I)), 'LAC proceeding')
    return {'id': hashlib.sha256((order['sha256'] + evidence).encode()).hexdigest()[:24],
            'type': action_type, 'text': text, 'actor': fact['actor'],
            'deadlineText': fact['deadlineText'], 'dueDate': due_date(order['orderDate'], fact['deadlineText'], order.get('nextHearingDate')),
            'state': 'Not confirmed complete', 'orderDate': order['orderDate'],
            'source': {'orderDate': order['orderDate'], 'page': fact['page'], 'evidence': evidence, 'officialUrl': order['officialUrl']}}

def usable_facts(order):
    """Retain independently validated facts; uncertainty is not contagious.

    Failed/partial extraction is never admitted. Conflicting land dimensions
    remain withheld, including when conflict spans separate inference chunks.
    """
    if order.get('status') != 'Validated' and not (order.get('status') == 'NeedsReview' and order.get('coverage',{}).get('allSelectedChunksProcessed') and not order.get('failureMessage')):
        return []
    facts=order.get('facts',[])
    conflicts=set()
    for field in ('compensation','possession'):
        values=[fact['value'].lower() for fact in facts if fact['field']==field]
        if any(re.search(r'\b(?:not|no|never|unpaid)\b',value) for value in values) and any(not re.search(r'\b(?:not|no|never|unpaid)\b',value) for value in values):
            conflicts.add(field)
    return [fact for fact in facts if fact['scope']!='Uncertain' and fact['field'] not in conflicts
            and (fact['field']!='compliance' or re.search(r'\b(?:complied|compliance|direction|directed)\b',fact['evidence'],re.I))]


def synthesize(case_id, case_number, orders):
    ordered = [dict(order,summaryFacts=usable_facts(order)) for order in sorted(orders, key=lambda x: x.get('orderDate') or '')]
    actions = []
    for order in ordered:
        for fact in order['summaryFacts']:
            if fact['scope'] == 'Current' and fact['field'] in ('compliance', 'supersession'):
                marker = r'filed|complied|completed|done|placed on record' if fact['field'] == 'compliance' else r'modified|replaced|superseded|set aside'
                # Deliberately strict: later evidence must explicitly cite both the original
                # date and verbatim action, otherwise it cannot silently close anything.
                for action in actions:
                    target_date=fact['targetOrderDate'] or (action['orderDate'] if action['orderDate'] in dates_in(fact['evidence']) else None)
                    target_action=fact['targetActionText'] or (action['text'] if action['text'] in fact['evidence'] else None)
                    if (target_date == action['orderDate']
                            and target_date in dates_in(fact['evidence'])
                            and target_action == action['text']
                            and action['text'] in fact['evidence'] and re.search(marker, fact['evidence'], re.I)):
                        action['state'] = 'Completed' if fact['field'] == 'compliance' else 'Superseded'
                        action['resolutionSource'] = {'orderDate': order['orderDate'], 'page': fact['page'],
                                                      'evidence': fact['evidence'], 'officialUrl': order['officialUrl']}
            action = office_action(fact, order)
            if action and not any(x['id'] == action['id'] for x in actions):
                actions.append(action)
    latest = ordered[-1] if ordered else None
    current = []
    if latest:
        for fact in latest['summaryFacts']:
            if fact['scope'] == 'Current' and fact['category'] in ('COURT_FINDING', 'PROCEDURAL_EVENT'):
                current.append({'text': fact['value'], 'source': {'orderDate': latest['orderDate'], 'page': fact['page'], 'evidence': fact['evidence'], 'officialUrl': latest['officialUrl']}})
    incomplete = any(o['status'] != 'Validated' for o in ordered)
    return {'version': 1, 'caseId': case_id, 'caseNumber': case_number,
            'status': 'NeedsReview' if incomplete else 'Validated',
            'currentPosition': current[:4], 'beforeNextHearing': [a for a in actions if a['state'] == 'Not confirmed complete'],
            'latestOrder': latest, 'orders': ordered, 'actions': actions,
            'notice': 'AI-assisted summary. Verify source evidence before official action.'}
