"""Presentation of already validated contingent evidence, never mandatory actions."""
import re
from semantics import usable_facts, SUBMISSION_ROLES, party_speech


def conditional_directions(order):
    result=[]
    for fact in usable_facts(order):
        text=fact['evidence']
        if fact['scope']!='Current' or fact['category'] in SUBMISSION_ROLES or party_speech(text): continue
        if fact['category'] not in ('COMPENSATION_FACT','PROCEDURAL_EVENT','COURT_DIRECTION','COURT_OBSERVATION','REFERENCE_FACT'): continue
        actor=re.search(r'\b(?:Land Acquisition Collector|LAC)\b',text,re.I)
        permission=re.search(r'\bmay release\b',text,re.I)
        contingent=re.search(r'\b(?:if (?:the )?need arises|if required)\b',text,re.I)
        if not actor or not (permission or contingent): continue
        if not re.search(r'examin|release|make a reference|consider the reference',text,re.I): continue
        condition=re.search(r'After examining the documents|if (?:the )?need arises|if required',text,re.I)
        # Exact textual conditions only; no deadline/overdue/completion inference.
        result.append(dict(label='Conditional / contingent Court direction',actor=actor.group(),
            text=fact['value'],scope=fact['scope'],conditionText=condition.group() if condition else None,
            modality=permission.group() if permission else 'Conditional',completion='Not established',
            source=dict(orderDate=order['orderDate'],page=fact['page'],evidence=text,officialUrl=order['officialUrl'],
                        **({'evidenceParts':fact['evidenceParts']} if fact.get('evidenceParts') else {})),
            evidenceParts=fact.get('evidenceParts')))
    return result
