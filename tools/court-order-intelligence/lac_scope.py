"""Order-local typed evidence. No office records, inferred jurisdiction, or model IDs.

The whole native order is scanned independently of model preselection. Unknown
syntax stays review work. Source roles/quotations are inherited from anchors.
"""
import copy
import hashlib
import re
from semantics import normalized, dates_in, party_speech, IMPERATIVE
from anchors import source_header, sentences

CONTRACT = 'court-lac-order-scope/v1'
SECTIONS = ('purposes', 'villages', 'awards', 'parcels', 'parcelGroups', 'possession',
            'compensation', 'directions', 'directionChanges', 'evidence', 'relevanceBasis')
LAC = re.compile(r'\b(?:Land Acquisition Collector|LAC)\b', re.I)
NUMBER = re.compile(r'(?<![\w/])\d+(?://|/)\d+(?:/\d+)*(?:\s*min\b)?(?![\w/])', re.I)
STRICT = re.compile(r'^([1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*)(?:\s*(min))?$', re.I)
AWARD = re.compile(r'\bAward\s+(?:No\.?|Number)\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9/.-]{1,49})', re.I)
VILLAGE = re.compile(r'\b(?:village|revenue estate|mauza)\s*(?:of\s+)?[:\-]?\s*["\x27]?([A-Z][A-Za-z]+(?:[ -][A-Z][a-z]+){0,3})\b')
DATE = r'(?:\d{1,2}[./-]\d{1,2}[./-](?:19|20)\d{2}|\d{1,2}\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{4})'
AREA = re.compile(r'\b(?:measuring|area\s*(?:of|:)?|admeasuring)\s*(\d+(?:\.\d+)?(?:\s*[-–]\s*\d+){0,2})\s*(bighas?|biswas?|acres?|hectares?|sq\.?\s*(?:yds?|yards?|m(?:etres?|eters?)?)|square\s+(?:yards?|metres?|meters?))\b', re.I)

def key(*parts):
    return hashlib.sha256('|'.join(map(str, parts)).encode()).hexdigest()[:24]

def fact(raw=None, value=None, evidence=None, attribution='OTHER', scope='Current', complete=True, review=False):
    return dict(rawText=raw, value=raw if value is None and raw is not None else value,
                state='NeedsReview' if review else 'Explicit' if raw is not None else 'NotStated' if complete else 'NeedsReview',
                attribution=attribution if raw is not None else None, scope=scope if raw is not None else None,
                evidenceIds=[evidence] if evidence else [])

def empty_scope(order, complete=False):
    return dict(contract=CONTRACT,
        source=dict(officialUrl=order['officialUrl'], orderDate=order.get('orderDate'), pdfSha256=order.get('sha256'),
                    sourceObservationId=order.get('sourceObservationId')),
        extraction=dict(state='Validated' if complete else 'NeedsReview', fullRelevantTextChecked=complete,
                        pageCount=0, extractorVersion='lac-scope-v1', reviewReasons=[]),
        lacRelevant=False, lacRelevanceState='NotRelevant' if complete else 'NeedsReview',
        lacAuthorityScope='UnknownLAC', lacActionable=False,
        missingState='NotStated' if complete else 'NeedsReview', **{s: [] for s in SECTIONS})

def authority_reference(match, text):
    # A numeric currency expression is not an office acronym. No fuzzy spelling.
    return not (match.group().lower() == 'lac' and (re.search(r'\d\s*$', text[:match.start()])
        or re.match(r'\s*(?:rupees|rs\b)', text[match.end():], re.I)))

def jurisdiction(text, start, end):
    suffix = text[end:end+100]
    found = re.match(r'\s*\(\s*([^()]{1,65})\s*\)', suffix)
    if not found:
        found = re.match(r'\s*[,\-]\s*(?:District\s+)([A-Za-z][A-Za-z -]{1,60}?)(?=\s+(?:shall|is|has|submits|states)|[,.]|$)', suffix, re.I)
    return found.group(1).strip() if found else None

def build_scope(order, pages, anchors=(), complete=True):
    result = empty_scope(order, complete)
    result['extraction']['pageCount'] = len(pages)
    headers, bodies = source_header(pages)
    roles = {(a['page'], a['text']): a for a in anchors}
    facts = {(f['page'], f['evidence']): f for f in order.get('facts', [])}
    passages = []
    # Explicit caption authority references count, without creating body facts.
    for page, text in headers.items():
        passages.append((page, normalized(text), 'Caption', None))
    for page, text in (bodies or {}).items():
        for _, _, passage in sentences(text):
            passage = normalized(passage)
            anchor = next((a for (p, t), a in roles.items() if p == page and t == passage), None)
            if anchor is None:
                anchor = next((a for (p,t),a in roles.items() if p==page and passage.endswith(t)),None)
                if anchor: passage=anchor['text']
            if anchor is None:
                # A quotation spanning pages keeps its historical role on each
                # local fragment, even though its original fact is cross-page.
                anchor=next((a for a in anchors if a.get('quoted') and any(
                    part.get('page')==page and (passage in normalized(part.get('evidence',''))
                        or normalized(part.get('evidence','')) in passage)
                    for part in a.get('evidenceParts',[]))),None)
            passages.append((page, passage, 'Body', anchor))
    if bodies is None:
        complete = False
        result = empty_scope(order, False)
        result['extraction']['pageCount'] = len(pages)
        result['extraction']['reviewReasons'].append('Caption/body boundary needs review')
        passages = [(p, normalized(t), 'Unknown', None) for p, t in pages.items()]
    for sequence, (page, text, location, anchor) in enumerate(passages):
        if not text: continue
        eid = key(order.get('sha256'), page, sequence, text)
        original = facts.get((page, text), {})
        quoted = bool(anchor and anchor.get('quoted'))
        role = 'HISTORICAL_QUOTATION' if quoted else (anchor or {}).get('speechRole') or original.get('category') or 'OTHER'
        if role == 'LAC_OR_RESPONDENT_SUBMISSION' or role == 'OTHER_PARTY_SUBMISSION': role = 'RESPONDENT_SUBMISSION'
        if role not in ('HISTORICAL_QUOTATION', 'PETITIONER_SUBMISSION', 'RESPONDENT_SUBMISSION', 'COURT_FINDING', 'COURT_DIRECTION', 'COURT_OBSERVATION'):
            role = 'OTHER'
        if party_speech(text) and role not in ('PETITIONER_SUBMISSION', 'RESPONDENT_SUBMISSION', 'HISTORICAL_QUOTATION'):
            role = 'PETITIONER_SUBMISSION' if re.search(r'\bpetitioner\b', text, re.I) else 'RESPONDENT_SUBMISSION'
        scope = 'Quoted' if quoted else original.get('scope', 'Current')
        span = normalized(pages[page]).find(text)
        evidence = dict(id=eid, page=page, paragraph=(anchor or {}).get('outerParagraph'),
                        anchorId=(anchor or {}).get('anchorId'), start=span if span >= 0 else None,
                        end=span+len(text) if span >= 0 else None, text=text, location=location,
                        attribution=role, scope=scope, extractionMethod='NativeText', spanBasis='WhitespaceNormalizedPage')
        before = sum(len(result[s]) for s in SECTIONS if s != 'evidence')
        refs = [m for m in LAC.finditer(text) if authority_reference(m, text)]
        for m in refs:
            result['relevanceBasis'].append(dict(term=m.group(), evidenceId=eid, location=location,
                authorityText=m.group(), jurisdiction=jurisdiction(text, m.start(), m.end())))
        if location != 'Body':
            if refs: result['evidence'].append(evidence)
            continue
        make = lambda raw=None, value=None, review=False: fact(raw, value, eid if raw is not None else None, role, scope, complete, review)
        local_villages = []
        for m in VILLAGE.finditer(text):
            vid = key(eid, 'village', m.start())
            local_villages.append(vid)
            result['villages'].append(dict(id=vid, name=make(m[1]), matchCandidate=normalized(m[1])))
        local_awards = []
        for m in AWARD.finditer(text):
            aid = key(eid, 'award', m.start())
            local_awards.append(aid)
            date = re.match(r'\s*(?:,\s*)?dated\s+(' + DATE + ')', text[m.end():], re.I)
            result['awards'].append(dict(id=aid, number=make(m[1].rstrip('.')), date=make(date[1], next(iter(dates_in(date[1])), None)) if date else make(),
                                        villageRefs=[], parcelRefs=[]))
        # Only a Khasra-labelled passage supplies parcel references. Other slash numbers are not parcels.
        parcel_matches = list(NUMBER.finditer(text)) if re.search(r'\bkhasra', text, re.I) else []
        parcel_matches = [m for m in parcel_matches if not any(m.start() >= a.start() and m.end() <= a.end() for a in AWARD.finditer(text))]
        local_parcels = []
        for index, m in enumerate(parcel_matches):
            pid = key(eid, 'parcel', m.start())
            local_parcels.append(pid)
            safe = STRICT.fullmatch(m.group())
            tail = text[m.end():parcel_matches[index+1].start() if index+1 < len(parcel_matches) else len(text)]
            area = AREA.search(tail)
            # A trailing collective area belongs to a group, not to its final parcel.
            individual = len(parcel_matches) == 1 or all(AREA.search(text[p.end():parcel_matches[i+1].start() if i+1 < len(parcel_matches) else len(text)]) for i,p in enumerate(parcel_matches))
            result['parcels'].append(dict(id=pid, number=make(m.group(), review=not bool(safe)),
                normalizedNumber=safe[1] if safe else None, qualifier=safe[2].lower() if safe and safe[2] else None,
                area=make(area[1], float(area[1])) if area and individual and re.fullmatch(r'\d+(?:\.\d+)?', area[1]) else make(area[1], review=True) if area and individual else make(),
                areaUnit=make(area[2]) if area and individual else make(),
                villageRefs=local_villages if len(local_villages)==1 else [],
                awardRefs=local_awards if len(local_awards)==1 and re.search(r'\b(?:under|covered by|acquired vide)\s+(?:the\s+)?Award\b|\bAward\b.{0,50}\b(?:covers|comprises|in respect of)', text, re.I) else []))
        if len(parcel_matches)>1 and AREA.search(text) and not all(AREA.search(text[p.end():parcel_matches[i+1].start() if i+1<len(parcel_matches) else len(text)]) for i,p in enumerate(parcel_matches)):
            a=AREA.search(text)
            result['parcelGroups'].append(dict(id=key(eid,'group'), parcelRefs=local_parcels,
                area=make(a[1], float(a[1])) if re.fullmatch(r'\d+(?:\.\d+)?',a[1]) else make(a[1],review=True), areaUnit=make(a[2])))
        # Back references describe only explicit relationships within this passage.
        for award in result['awards']:
            if award['id'] in local_awards:
                award['parcelRefs']=[p['id'] for p in result['parcels'] if award['id'] in p['awardRefs']]
                if award['parcelRefs'] and len(local_villages)==1: award['villageRefs']=local_villages
        if re.search(r'\bpossession\b',text,re.I):
            status='NeedsReview'
            if role in ('PETITIONER_SUBMISSION','RESPONDENT_SUBMISSION') or re.search(r'\b(?:claimed|alleged)\b',text,re.I): status='Claimed'
            elif re.search(r'\bdisputed\b',text,re.I): status='Disputed'
            elif re.search(r'possession\s+(?:(?:has|had)\s+)?(?:was|is|been)?\s*not\s+(?:been\s+)?taken',text,re.I): status='NotTaken'
            elif re.search(r'possession\s+(?:(?:has|had)\s+)?(?:was|is|been)?\s*taken',text,re.I): status='Taken'
            elif re.search(r'possession\s+(?:was\s+)?handed over',text,re.I): status='HandedOver'
            d=re.search(r'\b(?:taken|handed over)\s+on\s+('+DATE+')',text,re.I)
            result['possession'].append(dict(id=key(eid,'possession'),status=make(text,status, status=='NeedsReview'),language=make(text),
                date=make(d[1],next(iter(dates_in(d[1])),None)) if d else make(),parcelRefs=local_parcels,villageRefs=local_villages))
        if re.search(r'\b(?:compensation|payment|paid|deposited|tendered)\b',text,re.I):
            status='NeedsReview'
            submitted=role in ('PETITIONER_SUBMISSION','RESPONDENT_SUBMISSION','HISTORICAL_QUOTATION')
            future=bool(IMPERATIVE.search(text) or re.search(r'\b(?:may|will|would|seeks?|prays?)\b',text,re.I))
            if not submitted and not future:
                for label,pattern in [('Disputed',r'\bdisputed\b'),('PartlyPaid',r'\bpart(?:ly|ially) paid\b'),('Unpaid',r'\bunpaid\b|\bnot (?:been )?paid\b'),('Deposited',r'\b(?:was|is|has been|had been) deposited\b'),('Tendered',r'\b(?:was|is|has been|had been) tendered\b'),('Paid',r'\b(?:was|is|has been|had been) paid\b')]:
                    if re.search(pattern,text,re.I): status=label;break
            amount=re.search(r'\b(?:Rs\.?|INR)\s*([\d,]+(?:\.\d+)?)',text,re.I)
            rate=re.search(r'\b(?:Rs\.?|INR)\s*([\d,]+(?:\.\d+)?)\s*(?:/-)?\s*(?:per|/)\s*(bigha|acre|sq\.?\s*(?:yds?|yards?|m)|square\s+(?:yard|metre|meter))',text,re.I)
            beneficiary=re.search(r'\b(?:paid|payment)\s+to\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+){0,3})',text)
            deposit=re.search(r'\bdeposited\s+(?:in|with)\s+([^.;]{1,120})',text,re.I)
            result['compensation'].append(dict(id=key(eid,'compensation'),status=make(text,status,status=='NeedsReview'),language=make(text),
                amount=make(amount[1],float(amount[1].replace(',',''))) if amount and not rate else make(),currency=make(amount[0].split()[0],'INR') if amount else make(),
                rate=make(rate[1],float(rate[1].replace(',',''))) if rate else make(),rateUnit=make(rate[2]) if rate else make(),
                beneficiary=make(beneficiary[1]) if beneficiary else make(),depositReference=make(deposit[1]) if deposit else make(),parcelRefs=local_parcels,awardRefs=[]))
        # Actual judicial attribution must already have passed the original extraction guards.
        if original.get('category')=='COURT_DIRECTION' and role=='COURT_DIRECTION' and scope=='Current' and not quoted and not party_speech(text):
            actor=original.get('actor')
            if actor and actor in text and IMPERATIVE.search(text):
                deadline=original.get('deadlineText')
                hearing=re.search(r'\bnext\s+(?:hearing|date)\s*(?:is|on|:)\s*('+DATE+')',text,re.I)
                result['directions'].append(dict(id=key(eid,'direction'),directedTo=make(actor),action=make(text),object=make(text),
                    deadline=make(deadline) if deadline else make(),nextHearingDate=make(hearing[1],next(iter(dates_in(hearing[1])),None)) if hearing else make(),
                    modality='Conditional' if re.search(r'\bif\b|\bunless\b|subject to',text,re.I) else 'Permission' if re.search(r'\bmay\b',text,re.I) else 'Mandatory',
                    lacAuthorityScope='UnknownLAC',lacActionable=False,jurisdiction=jurisdiction(text,text.find(actor),text.find(actor)+len(actor)),
                    parcelRefs=local_parcels))
        if original.get('field') in ('supersession','compliance') and scope=='Current' and not party_speech(text) and not quoted:
            result['directionChanges'].append(dict(id=key(eid,'change'),kind=original['field'],language=make(text),
                targetOrderDate=original.get('targetOrderDate'),targetActionText=original.get('targetActionText'),evidenceIds=[eid]))
        for label,pattern in [('possession',r'\bpossession\b'),('compensation',r'\bcompensation\b'),('payment',r'\bpayment\b'),('reference petition',r'\breference petition\b|\breference\b.{0,40}\bsection\s+18\b|\bsection\s+18\b.{0,40}\breference\b'),('enhancement',r'\benhancement\b'),('award challenge',r'\b(?:challeng\w*|impugn\w*)\b.{0,70}\baward\b'),('status affidavit',r'\bstatus (?:affidavit|report)\b'),('compliance',r'\bcompliance\b'),('record production',r'\bproduce\b.{0,40}\brecord')]:
            if re.search(pattern,text,re.I): result['purposes'].append(dict(id=key(eid,'purpose',label),category=label,subject=make(text)))
        if sum(len(result[s]) for s in SECTIONS if s!='evidence')>before:
            result['evidence'].append(evidence)
    result['lacRelevant']=bool(result['relevanceBasis'])
    ambiguous_collector=any(re.search(r'\bCollector\b',t,re.I) and not LAC.search(t) for t in pages.values())
    result['lacRelevanceState']='Relevant' if result['lacRelevant'] else 'NeedsReview' if not complete or ambiguous_collector else 'NotRelevant'
    if result['lacRelevant'] or ambiguous_collector:
        result['extraction']['reviewReasons'].append('Office authority requires server-side jurisdiction resolution' if result['lacRelevant'] else 'Collector designation is ambiguous')
    if not complete or ambiguous_collector or any('NeedsReview' in str(x) for s in ('parcels','possession','compensation') for x in result[s]):
        result['extraction']['state']='NeedsReview'
    for label, pattern in [('villages',r'\b(?:village|revenue estate|mauza)\b'),('awards',r'\bAward\s+(?:No|Number)\b'),('parcels',r'\bkhasra\b')]:
        if not result[label] and any(re.search(pattern,text,re.I) for _,text,location,_ in passages if location=='Body'):
            result['extraction']['state']='NeedsReview'
            result['extraction']['reviewReasons'].append('Unresolved '+label+' reference syntax')
    if result['extraction']['state']=='NeedsReview': result['missingState']='NeedsReview'
    validate_scope(result,pages)
    return result

def validate_scope(result,pages):
    if result.get('contract')!=CONTRACT or any(not isinstance(result.get(s),list) for s in SECTIONS): raise ValueError('Invalid LAC scope contract')
    evidence={e['id']:e for e in result['evidence']}
    for e in evidence.values():
        if e['page'] not in pages or e['text'] not in normalized(pages[e['page']]): raise ValueError('Scope evidence is absent from cited page')
    def walk(value):
        if isinstance(value,list):
            for item in value: walk(item)
        elif isinstance(value,dict):
            if any(k in value for k in ('canonicalId','villageId','awardId','khasraId','confirmedMatch')): raise ValueError('Model canonical match prohibited')
            if 'rawText' in value:
                if value['state']=='NotStated' and (not result['extraction']['fullRelevantTextChecked'] or value['rawText'] is not None or value['value'] is not None): raise ValueError('Incomplete evidence cannot establish absence')
                if value['state']=='Explicit' and (not value['rawText'] or not value['evidenceIds']): raise ValueError('Explicit fact requires evidence')
                for eid in value['evidenceIds']:
                    if eid not in evidence or value['rawText'] not in evidence[eid]['text']: raise ValueError('Unsupported fact evidence')
            for item in value.values(): walk(item)
    walk(result)
    if result['lacActionable'] or result['lacAuthorityScope']!='UnknownLAC': raise ValueError('Extractor cannot authorize this office')
    if result['lacRelevanceState']=='NotRelevant' and not result['extraction']['fullRelevantTextChecked']: raise ValueError('Incomplete relevance scan')
    return result
