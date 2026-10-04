"""Independent validation and conservative, source-linked chronological synthesis."""
import calendar
import hashlib
import re
from datetime import date, timedelta
import jsonschema

VERSION = 'court-native-v3-chronology'
CATEGORIES = ['COURT_DIRECTION', 'COURT_FINDING', 'LAC_OR_RESPONDENT_SUBMISSION',
              'PETITIONER_SUBMISSION', 'OTHER_PARTY_SUBMISSION', 'PROCEDURAL_EVENT', 'HISTORICAL_LAND_FACT',
              'CASE_CONTEXT','ISSUE_BEFORE_COURT','COURT_OBSERVATION','DISPOSITION','LAND_FACT',
              'COMPENSATION_FACT','POSSESSION_FACT','REFERENCE_FACT','AWARD_FACT','KHASRA_FACT',
              'DOCUMENT_OR_FILING_FACT','RECORDED_COMPLIANCE','NEXT_HEARING','DEADLINE']
SUBMISSION_ROLES={'PETITIONER_SUBMISSION','LAC_OR_RESPONDENT_SUBMISSION','OTHER_PARTY_SUBMISSION'}
ROLE_FIELDS={'CASE_CONTEXT':'context','ISSUE_BEFORE_COURT':'issue','COURT_OBSERVATION':'observation','DISPOSITION':'disposition',
             'LAND_FACT':'land','COMPENSATION_FACT':'compensation','POSSESSION_FACT':'possession','REFERENCE_FACT':'referenceToAdj',
             'AWARD_FACT':'award','KHASRA_FACT':'khasra','DOCUMENT_OR_FILING_FACT':'filing','RECORDED_COMPLIANCE':'compliance',
             'NEXT_HEARING':'nextHearing','DEADLINE':'deadline'}
ATTRIBUTIONS={'COURT_DIRECTION':'The Court directed','COURT_FINDING':'The Court found','COURT_OBSERVATION':'The Court observed',
 'PETITIONER_SUBMISSION':'The petitioner stated','LAC_OR_RESPONDENT_SUBMISSION':'The LAC/respondent stated','OTHER_PARTY_SUBMISSION':'Another party stated',
 'PROCEDURAL_EVENT':'The Court recorded','DISPOSITION':'The Court decided','RECORDED_COMPLIANCE':'The Court recorded compliance',
 'CASE_CONTEXT':'Case context','ISSUE_BEFORE_COURT':'Issue before the Court','HISTORICAL_LAND_FACT':'Historical factual reference'}
FIELDS = ['direction', 'finding', 'compliance', 'supersession', 'village', 'khasra',
          'award', 'compensation', 'possession', 'section18', 'section30_31',
          'referenceToAdj', 'acquisitionSection', 'filing', 'documents', 'bench', 'nextHearing', 'context','issue','observation','disposition','land','deadline']
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
FACT['properties']['evidenceParts']={'type':'array','minItems':2,'maxItems':2,'items':{
    'type':'object','additionalProperties':False,'properties':{'page':{'type':'integer','minimum':1},
    'evidence':{'type':'string','minLength':1,'maxLength':900}},'required':['page','evidence']}}
SCHEMA = {'type': 'object', 'additionalProperties': False,
          'properties': {'facts': {'type': 'array', 'items': FACT, 'maxItems': 6},
                         'needsReview': {'type': 'boolean'}}, 'required': ['facts', 'needsReview']}

def normalized(text):
    return re.sub(r'\s+', ' ', text).strip()

def identity(text):
    # NO./NUMBER is a display label between a legal case type and its number,
    # never part of the registered case identity. Keep digits/year/type exact.
    text = re.sub(r'(?i)\b(?:no\.?|number)\s*(?=\d+\s*/\s*\d{4}\b)', '', text)
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

def confirmed_hearing_date(facts):
    court_dates={day for fact in facts if fact['field']=='nextHearing' and fact['scope']=='Current'
        and fact['category'] in ('COURT_DIRECTION','PROCEDURAL_EVENT','NEXT_HEARING')
        and re.search(r'\blist (?:the matter |matter )?before (?:the )?(?:Hon[’\x27]ble )?Court\b',fact['value'],re.I) for day in dates_in(fact['value'])}
    if len(court_dates)==1: return next(iter(court_dates))
    dates={day for fact in facts if fact['field']=='nextHearing' and fact['scope']=='Current'
           and fact['category'] in ('COURT_DIRECTION','PROCEDURAL_EVENT','NEXT_HEARING')
           for day in dates_in(fact['value'])}
    return next(iter(dates)) if len(dates)==1 else None

SUBMISSION = re.compile(r'\b(?:submits?|submitted|contends?|contended|alleges?|alleged|claims|claimed|asserts?|asserted|argues?|argued|prays?|seeks?|averred|undertakes?|prayer|according to|pointed out|it is stated)\b', re.I)
IMPERATIVE = re.compile(r'\b(?:is directed|are directed|shall|let .*?(?:file|place|furnish|stated)|it is directed|be filed|be placed on record|be listed|list (?:the matter |matter )?(?:on|for|before)|renotify|issue notice|we direct|Court directs|time is granted.{0,100}to file)\b', re.I)
OFFICE = re.compile(r'\b(?:LAC|Land Acquisition Collector|ADM[/ -]LAC|Collector)\b', re.I)

def party_speech(text):
    # A judicial disposition may mention a prayer, but actual speech/allegation
    # verbs are never erased by this narrowly scoped judicial reset.
    if re.search(r'\bis (?:set aside|allowed|dismissed)\b',text,re.I) and not re.search(r'\b(?:submits?|submitted|contends?|contended|claims|claimed|alleges?|alleged|asserts?|asserted|argues?|argued|prays?|seeks?|states?|stated|according to|pointed out)\b',text,re.I):
        text=re.sub(r'\bprayer\b','',text,flags=re.I)
    affidavit_statement=re.search(r'counter.affidavit',text,re.I) and not re.search(r'\bbe filed\b|\bshall file\b|\bis directed to file\b|\btime is granted.{0,100}to file\b',text,re.I)
    return bool(affidavit_statement or re.search(r'made the following submissions',text,re.I) or SUBMISSION.search(text) or re.search(
        r'\b(?:counsel|petitioner|respondent|LAC|Mr\.?|Ms\.?)\b[^.!?]{0,120}\b(?:states?|stated|reports?\s+that|reported\s+that)\b|\bsubmission of\b|\b(?:case|stand|contention) of the (?:petitioners?|respondents?|LAC) is that\b',text,re.I))

def proposition_kind(fact):
    """Modality is not a completed state. Classification creates no facts/actions."""
    text=fact['evidence']
    if fact['category'] in SUBMISSION_ROLES or party_speech(text): return 'Submission'
    if re.search(r'\b(?:if|unless|subject to|provided that|on condition|after examining)\b',text,re.I):
        return 'ConditionalDirection'
    if re.search(r'\b(?:may|permitted|permission|at liberty|can)\b',text,re.I): return 'Permission'
    if fact['category']=='RECORDED_COMPLIANCE': return 'RecordedCompliance'
    if fact['category']=='COURT_DIRECTION' or IMPERATIVE.search(text): return 'MandatoryDirection'
    return 'DescriptiveState'

def conflicting_fact_indexes(facts, include_submissions=False):
    """Opposed performed-state assertions on the same topic/predicate/scope/time.

    A field is a topic, not a truth axis: an assessment, deposit, payment and
    future permission can coexist. Unrecognized predicates are not guessed.
    Exact source dates distinguish historical states; scope is never collapsed.
    """
    groups={}
    for index,fact in enumerate(facts):
        kind=proposition_kind(fact)
        if kind not in ('DescriptiveState','RecordedCompliance') and not (include_submissions and kind=='Submission'): continue
        text=fact['value'].lower()
        predicates=re.findall(r'\b(?:paid|unpaid|taken|filed|deposited|assessed|released|made|completed|submitted|delivered)\b',text)
        if len(set(predicates))!=1: continue # Multi-state prose needs review, not guessed opposition.
        predicate='paid' if predicates[0]=='unpaid' else predicates[0]
        # An explicit Court finding may use field=finding while the same state
        # uses a topical field. Match its literal subject, not its model label.
        subject=text[:re.search(r'\b'+predicates[0]+r'\b',text).start()]
        subject=re.sub(r'^\s*\d+[.)]\s*','',subject)
        subject=re.sub(r'^.*?\b(?:court|we)\b.*?\b(?:finds?|found|holds?|held|observes?|observed|records?|recorded)\b\s+(?:that\s+)?','',subject)
        subject=re.sub(r'\b(?:therefore|accordingly|as of today|as per record)\b','',subject)
        subject=re.sub(r'\b(?:the|a|an|no|not|never|has|have|had|been|being|was|were|is|are|already|yet|now)\b','',subject)
        subject=identity(subject)
        if not subject: continue # Do not infer an omitted subject/semantic axis.
        time=tuple(sorted(dates_in(text)))
        attribution=fact['category'] if kind=='Submission' else 'RecordedState'
        identifiers=tuple(re.findall(r'\b\d+(?:[/-]\d+)*\b',text))
        key=(subject,predicate,fact['scope'],time,identifiers,identity(fact.get('actor') or ''),attribution)
        negative=bool(re.search(r'\b(?:not|no|never|unpaid)\b',text))
        groups.setdefault(key,[]).append((index,negative))
    return {index for group in groups.values() if len({negative for _,negative in group})>1
            for index,_ in group}

class CandidateSemanticError(ValueError):
    """A source candidate requires the single targeted semantic recovery."""


def compensation_only(value):
    # Test the selected proposition's predicate, not incidental topic words.
    text=normalized(value)
    predicate=re.search(r'\b(?:compensation|payment|deposit|amount|sum)\b.{0,65}\b(?:paid|unpaid|deposited|released|disbursed|made|received)\b|\b(?:pay|paid|deposit|deposited|release|released|disburse|disbursed)\b.{0,55}\b(?:compensation|payment|amount|sum)\b',text,re.I)
    land_state=re.search(r'\b(?:land|parcel|plot|khasra|title|ownership|possession|acquisition)\b.{0,70}\b(?:belongs|owned|owner|vested|acquired|situated|comprised|taken|disputed|proved)\b|\b(?:ownership|title|possession)\s+(?:of\b.{0,65})?(?:is|was|remains|has|had)\b',text,re.I)
    return bool(predicate and not land_state)


def validate(payload, pages):
    jsonschema.validate(payload, SCHEMA)
    for fact in payload['facts']:
        evidence = normalized(fact['evidence'])
        parts=fact.get('evidenceParts')
        if parts:
            if (parts[0]['page']!=fact['page'] or parts[1]['page']!=parts[0]['page']+1
                or normalized(' '.join(part['evidence'] for part in parts))!=evidence
                or any(part['page'] not in pages or normalized(part['evidence']) not in normalized(pages[part['page']]) for part in parts)):
                raise ValueError('Cross-page evidence is not on the exact adjacent supplied source pages')
        elif fact['page'] not in pages or evidence not in normalized(pages[fact['page']]):
            raise ValueError('Evidence is not on cited supplied source page')
        if normalized(fact['value']) not in evidence:
            raise ValueError('Fact value must be a verbatim part of its evidence')
        if fact['category']=='LAND_FACT' and compensation_only(fact['value']):
            raise CandidateSemanticError('Compensation-only predicate cannot validate as generic LAND_FACT/land')
        if fact['category'] in ROLE_FIELDS and fact['field']!=ROLE_FIELDS[fact['category']]:
            raise ValueError('Semantic role does not match its structured field')
        if fact['category'] not in SUBMISSION_ROLES and party_speech(evidence):
            raise ValueError('Party submission must retain party attribution, not become an established fact')
        role_patterns={'DEADLINE':r'\bwithin\b|\bbefore\b|\bby\s+\d',
                       'DISPOSITION':r'set aside|allowed|dismiss|disposed|disposal|closed',
                       'NEXT_HEARING':r'\blist\b|be listed|renotify|next (?:date|hearing)|adjourn'}
        if fact['category'] in role_patterns and not re.search(role_patterns[fact['category']],evidence,re.I):
            raise ValueError('Semantic role lacks explicit source support')
        if fact['field'] in ('direction', 'finding', 'compensation', 'possession', 'compliance'):
            for marker in (r'\bnot\b', r'\bno\b', r'\bnever\b', r'\bif any\b', r'\bpreferably\b'):
                if re.search(marker, evidence, re.I) and not re.search(marker, fact['value'], re.I):
                    raise ValueError('Fact omits a source negation or material condition')
        for field in ('actor', 'deadlineText'):
            if fact[field] and normalized(fact[field]) not in evidence:
                raise ValueError(field + ' is not supported by cited evidence')
        if fact['category'] in ('COURT_DIRECTION','COURT_FINDING','COURT_OBSERVATION','RECORDED_COMPLIANCE'):
            if party_speech(evidence):
                raise ValueError('Party submission cannot become a Court finding or direction')
            if fact['category']=='COURT_DIRECTION' and not IMPERATIVE.search(evidence):
                raise ValueError('Submission or nonoperative text cannot authorize a Court direction')
            page = normalized(pages[fact['page']])
            start = page.index(normalized(parts[0]['evidence']) if parts else evidence)
            prefix = page[max(0,start-240):start]
            # An operative setting-aside/allowing decision can mention the
            # petitioner's "prayer" without being a party submission. Do not
            # drop genuine speech verbs; this reset is judicial, not a claim.
            if re.search(r'\bis (?:set aside|allowed|dismissed)\b',prefix,re.I) and not re.search(r'\b(?:submits?|submitted|contends?|claims|claimed|alleges?|asserts?|argues?|prays?|seeks?|states?|stated)\b',prefix,re.I):
                prefix=re.sub(r'\bprayer\b','',prefix,flags=re.I)
            fresh_procedure=re.match(r'^(?:\d+\.\s*)?(?:(?:Accordingly,\s*)?let\b|renotify|list (?:the matter |matter )?(?:on|for|before)|issue notice)\b',evidence,re.I)
            if party_speech(prefix) and not fresh_procedure and not re.search(r'(?:Court|we) (?:direct|order|hold|observe|find|note)|it is directed', prefix, re.I):
                raise ValueError('Direction excerpt drops nearby party-submission attribution')
        if fact['field'] in ('compliance', 'supersession'):
            if fact['category'] not in ('COURT_FINDING', 'PROCEDURAL_EVENT','RECORDED_COMPLIANCE') or party_speech(evidence):
                raise ValueError('Party claim cannot close an office direction')
            if fact['field'] == 'compliance' and not (re.search(r'\b(?:complied|compliance|direction|directed)\b', evidence, re.I) and re.search(r'\b(?:filed|complied|completed|done|placed on record|compliance)\b', evidence, re.I)):
                raise ValueError('Compliance requires an explicit obligation and recorded performance')
        if fact['scope'] == 'Uncertain':
            payload['needsReview'] = True
    # Opposed assertions on the same land dimension are review work, never a chosen fact.
    if conflicting_fact_indexes(payload['facts'],include_submissions=True): payload['needsReview'] = True
    return payload

def office_action(fact, order):
    if proposition_kind(fact) != 'MandatoryDirection': return None
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
    conflicts=conflicting_fact_indexes(facts)
    admitted=[fact for index,fact in enumerate(facts) if fact['scope']!='Uncertain' and (fact['category'] in SUBMISSION_ROLES or not party_speech(fact['evidence']))
            and index not in conflicts
            and (fact['field']!='compliance' or re.search(r'\b(?:complied|compliance|direction|directed)\b',fact['evidence'],re.I))]
    result=[]
    for fact in admitted:
        # Legacy/small-model generic "finding" labels cannot turn Court history
        # (e.g. an award challenged before the High Court) into a judicial finding.
        # Preserve the source fact as context, not an invented conclusion. The
        # office-action/compliance lifecycle categories are deliberately untouched.
        if fact['category']=='COURT_FINDING' and fact['field'] not in ('compliance','supersession'):
            text=fact['evidence']
            finding=re.search(r'\b(?:Court|we|I)\b.{0,70}\b(?:finds?|found|holds?|held|conclud\w*|of the view)\b|\b(?:opinion of the Court|it is (?:clear|evident)|it becomes clear|in view of|accordingly)\b',text,re.I)
            observation=re.search(r'\bCourt\b.{0,70}\b(?:observ\w*|notes?|noted|record\w*|perused|considered)\b',text,re.I)
            if not finding:
                fact=dict(fact,category='COURT_OBSERVATION' if observation else 'CASE_CONTEXT',field='observation' if observation else 'context')
        result.append(fact)
    return result


def propositions(order):
    result=[]
    for fact in usable_facts(order):
        source={'orderDate':order.get('orderDate'),'page':fact['page'],'evidence':fact['evidence'],'officialUrl':order['officialUrl']}
        if fact.get('evidenceParts'): source['evidenceParts']=fact['evidenceParts']
        role=fact['category']
        # Compatibility with validated V1 facts: an explicit present judicial
        # disposal is a disposition, not a generic background finding. No new
        # factual text or actor is created; historical/quoted scope is retained.
        if role in ('COURT_FINDING','PROCEDURAL_EVENT','CASE_CONTEXT') and re.search(r'\b(?:petition|application|appeal|order)\b.{0,180}\b(?:is|are|stands|accordingly)\b.{0,60}\b(?:disposed|dismissed|allowed|set aside)\b',fact['evidence'],re.I):
            role='DISPOSITION'
        result.append({'id':hashlib.sha256((str(order.get('sha256'))+str(source)+role).encode()).hexdigest()[:24],
          'role':role,'attribution':ATTRIBUTIONS.get(role,'The record shows'),'scope':fact['scope'],'actor':fact.get('actor'),
          'field':fact['field'],'text':fact['value'],'source':source,
          'structuredValues':{fact['field']:fact['value'],'actor':fact.get('actor'),'deadlineText':fact.get('deadlineText'),'targetOrderDate':fact.get('targetOrderDate')}})
    return result


def order_digest(order):
    # Meaningful facts are independent of office obligations. Reserve room for
    # distinct roles; never suppress an attributed party position as "no action".
    entries=[entry for entry in propositions(order) if entry['scope']!='Quoted']
    # Reserve a slot for the outcome even if several other roles appeared first.
    entries.sort(key=lambda entry:0 if entry['role']=='DISPOSITION' else 1)
    chosen=[]
    for entry in entries:
        if not any(item['role']==entry['role'] for item in chosen): chosen.append(entry)
        if len(chosen)>=6: break
    for entry in entries:
        if len(chosen)>=6: break
        if entry not in chosen: chosen.append(entry)
    return chosen


def synthesize(case_id, case_number, orders):
    from conditional_directions import conditional_directions
    from chronology import factual_events, source_coverage, presentation_kind
    from order_index import index_entries
    ordered = [dict(order,summaryFacts=usable_facts(order),propositions=propositions(order),digest=order_digest(order)) for order in sorted(orders, key=lambda x: x.get('orderDate') or '')]
    for order in ordered: order['presentationKind']=presentation_kind(order)
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
    for order in ordered:
        order['officeActionCount']=sum(office_action(fact,order) is not None for fact in order['summaryFacts'])
    # Latest representative evidence for each role/topic, then chronological
    # presentation. Statements remain observations/submissions, not one voice.
    position={}
    for order in ordered:
        for entry in order['propositions']:
            if entry['scope']=='Quoted': continue
            key=(entry['role'],entry['field'])
            previous=position.get(key)
            issue=r'\b(?:seeking|concerns?|challeng\w*|impugn\w*|dispute)\b'
            if entry['role']=='CASE_CONTEXT' and previous and previous['source']['orderDate']==entry['source']['orderDate'] and re.search(issue,previous['text'],re.I) and not re.search(issue,entry['text'],re.I):
                continue # Administrative filler must not replace the case issue.
            principal=r'\b(?:petition|appeal|suit)\b.{0,100}\b(?:allowed|dismissed|disposed)\b'
            if (entry['role']=='DISPOSITION' and previous and previous['scope']=='Current'
                and previous['source']['orderDate']==entry['source']['orderDate']
                and re.search(principal,previous['text'],re.I) and not re.search(principal,entry['text'],re.I)):
                continue # Ancillary applications cannot replace the principal outcome.
            position[key]=entry
    # Preserve different voices in the small brief rather than filling its final
    # six slots with repeated land fields from a single party.
    representatives=list(position.values())
    priority=['CASE_CONTEXT','ISSUE_BEFORE_COURT','DISPOSITION','COURT_DIRECTION','COURT_FINDING',
              'PETITIONER_SUBMISSION','LAC_OR_RESPONDENT_SUBMISSION','COURT_OBSERVATION',
              'OTHER_PARTY_SUBMISSION','PROCEDURAL_EVENT','RECORDED_COMPLIANCE']
    selected=[]
    for role in priority:
        candidates=[entry for entry in representatives if entry['role']==role]
        if role=='COURT_FINDING': candidates=[entry for entry in candidates if entry['field']=='finding'] or candidates
        if candidates: selected.append(candidates[-1])
        if len(selected)==6: break
    for entry in reversed(representatives):
        if len(selected)==6: break
        if entry not in selected: selected.append(entry)
    # A stale adjournment/submission must not lead today's case brief. Keep the
    # core dispute, then newest developments; history retains every old fact.
    current=[]
    recent_directions=[entry for entry in latest['propositions']
        if entry['role']=='COURT_DIRECTION' and entry['scope']=='Current' and entry['field']!='nextHearing'] if latest else []
    selected=recent_directions[:2]+selected
    selected.sort(key=lambda entry:(entry['role'] in ('CASE_CONTEXT','ISSUE_BEFORE_COURT'),
                                    entry['source']['orderDate'] or ''),reverse=True)
    for entry in selected:
        if any(normalized(previous['text'])==normalized(entry['text']) for previous in current): continue
        if (latest and entry['source']['orderDate']!=latest['orderDate']
                and entry['role'] in ('PETITIONER_SUBMISSION','PROCEDURAL_EVENT')
                and re.search(r'passover|adjourn|bench.*assemble',entry['text'],re.I)): continue
        current.append(entry)
    # If the newest source failed, do not present older sources as current.
    if latest and not latest['summaryFacts']: current=[]
    incomplete = any(o['status'] != 'Validated' for o in ordered)
    final = latest if latest and any(entry['role']=='DISPOSITION' and entry['scope']=='Current'
        and re.search(r'\b(?:petition|appeal|suit)\b.{0,100}\b(?:allowed|dismissed|disposed)\b',entry['text'],re.I)
        for entry in latest['propositions']) else None
    return {'version': 1, 'semanticVersion':VERSION, 'caseId': case_id, 'caseNumber': case_number,
            'status': 'NeedsReview' if incomplete else 'Validated',
            'currentPosition': current[:6], 'beforeNextHearing': [a for a in actions if a['state'] == 'Not confirmed complete'],
            'conditionalDirections': [entry for order in ordered for entry in conditional_directions(order)],
            'latestOrder': latest, 'finalOrder': final, 'caption': latest.get('caption',{}) if latest else {},
            'latestMeaningfulOrder':next((order for order in reversed(ordered) if order['presentationKind']=='Substantive'),None),
            'chronologyWarnings': ['An earlier source contains a final disposition but later orders also exist. Intervening restoration/appeal history is not established by these supplied sources.']
                if any(order['orderDate']!=latest['orderDate'] and any(entry['role']=='DISPOSITION'
                    and re.search(r'\b(?:petition|appeal|suit)\b.{0,100}\b(?:allowed|dismissed|disposed)\b',entry['text'],re.I)
                    for entry in order['propositions']) for order in ordered) else [],
            'lacCaptionAppearances': [order['caption']['respondentAdvocates'] for order in ordered
                if re.search(r'\bLAC\b|Land Acquisition Collector',order.get('caption',{}).get('respondentAdvocates',{}).get('text',''),re.I)],
            'factualChronology': factual_events(ordered), 'sourceCoverage':source_coverage(ordered),
            'caseBrief': {role: [entry for order in ordered for entry in order['propositions'] if entry['role']==role and entry['scope']!='Quoted']
                          for role in ATTRIBUTIONS},
            'orderIndex':index_entries(case_id,case_number,ordered),
            'orders': ordered, 'actions': actions,
            'notice': 'AI-assisted summary. Verify source evidence before official action.'}
