"""One bounded model-only recovery of omitted high-signal selected evidence."""
import json
from anchors import INSTRUCTIONS, schema_for, expand, requires_present_scope
from semantics import validate
from preselection import OPERATIVE, JUDICIAL, TOPIC, PRAYER, LAC_POSITION, bounded_chunks


def recover(candidates, facts, pages, case_number, order_date, provider):
    represented=set()
    for fact in facts:
        if fact['scope']=='Uncertain': continue
        anchor=next((a for a in candidates if (a['page'],a['text'])==(fact['page'],fact['evidence'])),None)
        if anchor and requires_present_scope(anchor) and fact['scope']!='Current': continue
        try: validate(dict(facts=[fact],needsReview=False),pages)
        except (ValueError,TypeError,KeyError): continue
        represented.add((fact['page'],fact['evidence']))
    high=[a for a in candidates if not a.get('quoted') and
          (requires_present_scope(a) or PRAYER.search(a['text']) or OPERATIVE.search(a['text']) or
           JUDICIAL.search(a['text']) and TOPIC.search(a['text']) or
           LAC_POSITION.search(a['text']) and TOPIC.search(a['text']))]
    missing=[a for a in high if (a['page'],a['text']) not in represented]
    audit=dict(used=False,needsReview=False,uncoveredAnchorIds=[a['anchorId'] for a in missing],remainingAnchorIds=[])
    if not missing: return [],audit
    chunk=bounded_chunks(missing)[0]
    audit['used']=True; audit['attemptedAnchorIds']=[a['anchorId'] for a in chunk]
    source=json.dumps(dict(documentOrderDate=order_date,caseNumber=case_number,
        sourceRoleContext=pages[1][:1800],anchors=chunk),ensure_ascii=False)
    try:
        feedback='Resolve only these unresolved source candidates. Unquoted explicit present-state context cannot use Historical or Uncertain scope; compensation-only predicates cannot use LAND_FACT/land. Select only source-supported compatible classifications, or omit unresolved evidence for review. Do not invent facts.'
        output=expand(provider.extract(INSTRUCTIONS,source,schema_for(chunk,pages),feedback),chunk,pages)
        extra=output['facts']
        audit['needsReview']=output['needsReview']
    except MemoryError:
        raise
    except Exception as error:
        # One attempt only, no transport replay, no direct candidate-to-fact copy.
        extra=[]; audit['failureType']=type(error).__name__; audit['needsReview']=True
    represented.update((f['page'],f['evidence']) for f in extra if f['scope']!='Uncertain')
    audit['remainingAnchorIds']=[a['anchorId'] for a in missing if (a['page'],a['text']) not in represented]
    return extra,audit
