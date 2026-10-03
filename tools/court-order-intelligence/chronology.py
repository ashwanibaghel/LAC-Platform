"""Source-only caption context and factual dates, never canonical Court records."""
import re
from semantics import normalized, dates_in


def bench_text(value):
    """Strip an oral-author repeat, never infer an additional bench member.

    Caption/body splitting can leave the author's name before ', J. (Oral)'
    in the header. Only remove a trailing full name already introduced as
    JUSTICE; retain the unmodified passage separately as evidence.
    """
    value = normalized(re.split(r'\bThrough\s*:|\b(?:O R D E R|J U D G M E N T)\b', value, flags=re.I)[0])
    value = re.split(r',?\s+J\.?\s*\(\s*oral\s*\)', value, flags=re.I)[0].strip()
    names = re.findall(r'\bJUSTICE\s+(.+?)(?=\s+(?:HON[\x27’]BLE|JUSTICE)\b)', value, re.I)
    for name in names:
        if value.lower().endswith(' ' + name.lower()):
            return value[:-(len(name)+1)].strip()
    last = re.search(r'\bJUSTICE\s+(.+)$', value, re.I)
    if last:
        words = last[1].split()
        if len(words) >= 4 and len(words) % 2 == 0:
            middle = len(words)//2
            if [w.lower() for w in words[:middle]] == [w.lower() for w in words[middle:]]:
                value = value[:last.start(1)] + ' '.join(words[:middle])
    return value


def caption_context(headers, official_url, order_date):
    """Keep exact caption blocks and page evidence; do not infer advocate roles."""
    result = {}
    for page, text in headers.items():
        for role, pattern in (
            ('petitioner', r'([A-Z][A-Z .,&()\-/]+?)\s*\.{2,}\s*Petitioners?\b'),
            ('respondent', r'([A-Z][A-Z .,&()\-/]+?)\s*\.{2,}\s*Respondents?\b'),
            ('bench', r'CORAM:\s*(.+)$'),
        ):
            match = re.search(pattern, text)
            if match:
                value = normalized(match[1])
                if role == 'bench':
                    value = bench_text(value)
                # Caption number preceding the title is not part of the party name.
                value = re.sub(r'^CM APPL\..*?\d{4}\s+', '', value)
                result[role] = {'text': value, 'source': {'page': page, 'orderDate': order_date,
                    'officialUrl': official_url, 'evidence': match[0]}}
        for match in re.finditer(r'\.{2,}\s*(Petitioners?|Respondents?)\b\s*Through:\s*(.*?)(?=\bversus\b|CORAM:|$)', text, re.I):
            party = 'petitioner' if match[1].lower().startswith('petition') else 'respondent'
            value = normalized(match[2]).rstrip(' .')
            if value and len(value) <= 900:
                result[party + 'Advocates'] = {'text': value, 'source': {'page': page,
                    'orderDate': order_date, 'officialUrl': official_url, 'evidence': match[0]}}
    if 'petitioner' in result and 'respondent' in result:
        result['title'] = result['petitioner']['text'] + ' vs ' + result['respondent']['text']
    return result


def factual_events(orders):
    """A mentioned date remains a dated factual reference with its entire attribution.

    Never split a multi-date sentence into invented event/date associations. Such
    passages explicitly show all mentioned dates instead of claiming a single event.
    """
    events = []
    seen = set()
    for order in orders:
        for entry in order.get('propositions', []):
            if entry['scope'] in ('Quoted', 'Uncertain') or entry['role'] in ('COURT_DIRECTION', 'NEXT_HEARING', 'DEADLINE'):
                continue
            dates = sorted(dates_in(entry['text']))
            if not dates:
                continue
            key = (tuple(dates), entry['text'], entry['role'])
            if key in seen:
                continue
            seen.add(key)
            events.append({'kind': 'FactualReference', 'dates': dates, 'text': entry['text'],
                           'role': entry['role'], 'attribution': entry['attribution'], 'source': entry['source']})
    return sorted(events, key=lambda entry: entry['dates'][0])


def source_coverage(orders):
    return {'basis': 'Explicitly supplied official sources; completeness beyond these sources is not established',
            'knownSources': len(orders),
            'checkedSources': sum(bool(order.get('summaryFacts')) for order in orders),
            'gaps': [{'orderDate': order.get('orderDate'), 'officialUrl': order['officialUrl'],
                      'reason': order.get('failureMessage') or 'No verified facts available'}
                     for order in orders if not order.get('summaryFacts')]}

def presentation_kind(order):
    """Content-based display only; never changes extraction or discards orders."""
    entries=[entry for entry in order.get('propositions',[]) if entry['scope'] not in ('Quoted','Uncertain')]
    if not entries: return 'Unverified'
    routine=re.compile(r'\blist\b|renotify|adjourn|passover|bench.*assemble|hybrid mode|time.*(?:grant|file)|within.*weeks|be filed within',re.I)
    return 'Routine' if all(entry['role'] in ('COURT_DIRECTION','PROCEDURAL_EVENT','CASE_CONTEXT','PETITIONER_SUBMISSION','LAC_OR_RESPONDENT_SUBMISSION')
                           and routine.search(entry['text']) for entry in entries) else 'Substantive'
