"""Bounded USER intent inheritance. Conversation text never becomes evidence."""
import re
from query_intents import normalize
from order_index import requested_date

FOLLOW_UP = re.compile(r'^\s*(?:aur\b|and\b|also\b|what about\b|iski\b|iska\b|us\b|uss\b|usme\b|usmein\b|is\s+order\b|that\b|this\b|it\b)|\b(?:us par|is par|its deadline)\b|उस|इसकी|उसकी|और', re.I)
SUBJECT_TOPICS = {'compensation','possession','award','khasra','reference','section18','section30_31','filing'}

def resolve(question, recent_user_questions, order_dates):
    current = normalize(question)
    if not FOLLOW_UP.search(question): return question, current
    previous = [q for q in (recent_user_questions if isinstance(recent_user_questions,list) else [])[-4:] if isinstance(q,str) and 0<len(q)<=600]
    if not previous: return question, current
    prior_question,prior = resolve(previous[-1],previous[:-1],order_dates)
    # An explicit new speaker or predicate wins. In particular, a petitioner's
    # prayer must not constrain a follow-up asking for the Court's position.
    if current['topics'] == ['unknown']:
        current['topics'] = ['party_position'] if current['party'] else prior['topics'][:]
        if current['party'] is None: current['party'] = prior['party']
    else:
        current['topics'] = list(dict.fromkeys(current['topics']+[t for t in prior['topics'] if t in SUBJECT_TOPICS]))[:3]
    target = requested_date(question,order_dates)
    if not target['requested']: current['latest'] = current['latest'] or prior['latest']
    if any(t in current['topics'] for t in ('direction','lac_action')) and 'directionClass' not in current and 'directionClass' in prior:
        current['directionClass'] = prior['directionClass']
    if 'timeline' in current['topics'] and 'lastOrderCount' not in current and 'lastOrderCount' in prior:
        current['lastOrderCount'] = prior['lastOrderCount']
    if current['yearFrom'] is None:
        current['yearFrom'],current['yearTo'] = prior['yearFrom'],prior['yearTo']
    prior_target = requested_date(prior_question,order_dates)
    if not target['requested'] and prior_target['requested'] and prior_target['date'] in order_dates:
        question += '\nOrder date: '+prior_target['date']
    return question,current
