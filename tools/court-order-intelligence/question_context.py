"""Resolve referents only; history never contributes a fact or model prompt."""
import re
from query_intents import normalize
from semantics import usable_facts
from order_index import requested_date

FOLLOW_UP = re.compile(r'\b(?:us|usse|usme|isme|ye|yeh)\b|\b(?:this|that)\s+(?:order|direction)\b|\b(?:in|about) it\b|उस\s*(?:आदेश|निर्देश)|इसमें|उसमें|ये\s*निर्देश|यह\s*निर्देश',re.I)

def resolve(artifact, case_id, question, context=None, provider=None):
    intent=normalize(question, None if FOLLOW_UP.search(question) else provider)
    target=requested_date(question,[o.get('orderDate') for o in artifact.get('orders',[])])
    if target['requested'] or intent['latest'] or intent.get('fullStory') or not FOLLOW_UP.search(question):
        return intent, None
    if 'lac_action' in intent['topics'] and re.search(r'\b(?:now|current|ab|hai|must)\b|अभी|आज|करना है',question,re.I):
        # Present office action is always governed by the current lifecycle,
        # never by an earlier referent's historical direction.
        return intent,None
    turns=context.get('turns',[]) if isinstance(context,dict) and context.get('caseId')==case_id else []
    focus=[]
    for turn in turns[-1:] if isinstance(turns,list) else []:
        if not isinstance(turn,dict) or not isinstance(turn.get('question'),str) or len(turn['question'])>600: continue
        sources=turn.get('sources',[])
        if not isinstance(sources,list): continue
        # Match both source URL and date to currently admitted facts. Never trust
        # the old answer, old claim text or a stale source reference.
        admitted=[s for s in sources[:8] if isinstance(s,dict) and any(
            s.get('orderDate')==o.get('orderDate') and s.get('officialUrl')==o.get('officialUrl') and usable_facts(o)
            for o in artifact.get('orders',[]))]
        if admitted: focus=admitted
    if len({(s['orderDate'],s['officialUrl']) for s in focus})!=1:
        return intent, 'FollowUpReferentUnavailable'
    intent['focusSource']=focus[0]
    intent['latest']=False
    if re.search(r'kis date|which date|what date|किस तारीख|किस दिन',question,re.I):
        intent['topics']=['order_summary']; intent['referentDate']=True
        intent['factualDates']=False # The source date need not occur inside its relative-deadline passage.
    elif re.search(r'(?:lac|एल.?ए.?सी).*(?:karna|do|करना)',question,re.I):
        intent['topics']=['direction']; intent['historicalOffice']=True
    elif intent['topics']==['unknown']:
        intent['topics']=['order_summary']
    return intent,None
