"""Source-only caption context and factual dates, never canonical Court records."""
import re
from semantics import normalized, dates_in


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
                    first=re.search(r"HON'BLE (?:MR\.|MS\.) JUSTICE (.*?)(?= HON'BLE|$)",value)
                    if first and value.count(first[1])>1 and value.endswith(' '+first[1]):
                        value=value[:-(len(first[1])+1)]
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
