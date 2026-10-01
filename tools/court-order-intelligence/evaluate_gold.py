"""Read-only factual-recall audit; no model, network, DB or training operation."""
import argparse
import json
from pathlib import Path
from semantics import normalized, propositions


def evaluate(gold, order):
    entries = propositions(order)
    results = []
    for check in gold['checklist']:
        if check.get('captionField') == 'identity':
            candidates = [{'text':order.get('rawIdentity',''), 'source':{'page':order.get('identityPage')}}]
        elif check.get('captionField'):
            candidates = [order.get('caption',{}).get(check['captionField'],{})]
        else:
            candidates = [entry for entry in entries if entry['scope'] not in ('Quoted','Uncertain')]
        matched = any(all(normalized(part).lower() in normalized(candidate.get('text','')).lower()
                          for part in check['contains'])
                      and candidate.get('source',{}).get('page') == check['page']
                      and (not check.get('role') or candidate.get('role') == check['role'])
                      and (not check.get('scope') or candidate.get('scope') == check['scope'])
                      for candidate in candidates)
        results.append({'id':check['id'], 'label':check['label'], 'represented':matched})
    return {'caseNumber':gold['caseNumber'], 'sourceShaMatches':gold['sha256']==order.get('sha256'),
            'represented':sum(check['represented'] for check in results), 'total':len(results),
            'checks':results, 'note':'Recall checklist only, not a confidence score. Correctness/usefulness still require full-source review.'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--gold',required=True)
    parser.add_argument('--artifact',required=True)
    args = parser.parse_args()
    gold = json.loads(Path(args.gold).read_text(encoding='utf-8'))
    artifact = json.loads(Path(args.artifact).read_text(encoding='utf-8'))
    audits=[]
    for source in gold.get('orders',[gold]):
        source=dict(source,caseNumber=gold['caseNumber'])
        order=next((order for order in artifact.get('orders',[artifact]) if order['officialUrl']==source['officialUrl']),None)
        audits.append(evaluate(source,order) if order else {'orderDate':source['orderDate'],'missingSource':True})
    print(json.dumps(audits,ensure_ascii=False,indent=2))
