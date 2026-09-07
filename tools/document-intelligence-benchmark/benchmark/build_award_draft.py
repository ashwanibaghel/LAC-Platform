"""Build a read-only, evidence-linked draft from local OCR/table outputs."""
from __future__ import annotations
import argparse, json, re, time
from pathlib import Path

def page_text(path: Path) -> str:
    items=json.loads(path.read_text(encoding='utf-8'))
    rows={}
    for item in items:
        y=round(float(item['box'][0][1])/8)*8
        rows.setdefault(y,[]).append((float(item['box'][0][0]),str(item.get('txt',''))))
    return '\n'.join(' '.join(t for _,t in sorted(v)) for _,v in sorted(rows.items()))

def candidate(section, value, page, evidence, reason='Needs human review'):
    return {'section':section,'detectedValue':value,'sourcePage':page,'sourceEvidence':evidence,'status':'NeedsReview','needsHumanInterpretation':True,'reason':reason}

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); args=ap.parse_args()
    root=args.root; raw=root/'real-output'/'full-award-rapidocr'; analysis=json.loads((root/'real-output'/'award-page-analysis.json').read_text(encoding='utf-8'))
    out=[]; concepts={}; alltext={}
    for item in analysis['pages']:
        p=item['page']; text=page_text(raw/f'page-{p}.raw.json'); alltext[p]=text; concepts[p]=item['concepts']
        if p==1:
            patterns=[('Award Number',r'award\s*no\.?\s*[:\-]?\s*([^\n]+)'),('Village',r'(?:name\s+of\s+village|village)\s*[:\-]?\s*([^\n]+)'),('Purpose/Nature',r'(?:purpose|nature)\s+of\s+acquisition\s*[:\-]?\s*([^\n]+)'),('Notifications',r'notification\s*[:\-]?\s*([^\n]+)')]
            for sec,pat in patterns:
                m=re.search(pat,text,re.I)
                if m: out.append(candidate(sec,m.group(1).strip(),p,'OCR page 1','Identity text extracted; exact value requires human confirmation'))
        # Keep every table output as evidence, never silently promote values.
        j=root/'real-output'/'table-candidates'/f'page-{p}'/'join.json'
        if j.exists():
            data=json.loads(j.read_text(encoding='utf-8'))
            for table in data.get('tables',[]):
                label='Table evidence'
                if 'CWP/court table' in item['concepts']: label='Court Cases / CWPs'
                elif 'Award Khasra table' in item['concepts']: label='Khasras / Areas'
                elif 'classification' in ' '.join(item['concepts']).lower(): label='Land Classification'
                out.append(candidate(label,f"{table['rows']} rows x {table['columns']} columns",p,'Table Transformer geometry + RapidOCR words',f"{table['uncertain_words']} words fell outside a unique cell"))
        for sec,terms in [('Possession',['possession','dispossession','status quo','stay']),('Claims',['claim','compensation','claimant']),('Area Issues / Corrigendum',['corrigendum','revised area','area issue']),('Valuation Rules',['valuation','solatium','interest']),('Compensation Rules',['compensation','award amount']),('Supplementary Matters',['supplementary','additional'])]:
            if any(t in text.lower() for t in terms): out.append(candidate(sec,'Narrative evidence detected',p,'OCR text on page', 'Narrative context is preserved; no inference or auto-commit'))
    # A conservative count of Khasra-like tokens is useful for review sizing, not canonical data.
    khasra=[]
    for p,text in alltext.items():
        for m in re.finditer(r'(?<![A-Za-z0-9])\d{1,4}\s*/\s*(?:\d{1,4}|\d{1,4}\s*/\s*\d{1,4})(?:\s*(?:min|m|/\d+))?',text,re.I):
            khasra.append(candidate('Khasras',m.group(0),p,'OCR token on source page','Qualifier/digit interpretation deliberately deferred'))
    out.extend(khasra)
    summary={'pagesProcessed':len(alltext),'ocrPages':len(alltext),'embeddedTextPages':0,'tableCandidatePages':[x['page'] for x in analysis['pages'] if x.get('table_candidate')],'candidateCount':len(out),'khasraTokenCount':len(khasra),'needsReviewCount':len(out),'unreadableCount':0,'invalidOrCouldNotInterpretCount':0,'canonicalCommitPerformed':False}
    result={'generatedAtUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'mode':'read-only experimental draft','summary':summary,'candidates':out,'pageText':alltext,'pageConcepts':concepts}
    args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps(summary,indent=2))
if __name__=='__main__': main()
