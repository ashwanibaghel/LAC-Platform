"""Experimental high-precision domain interpretation over ignored benchmark outputs."""
from __future__ import annotations
import argparse,json,re,time
from pathlib import Path

STRICT=re.compile(r'^(?P<number>[1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*)(?:\s*(?P<qualifier>min))?$',re.I)
LOOSE=re.compile(r'(?<![A-Za-z0-9])\d{1,4}\s*(?:/|//)\s*\d{1,4}(?:\s*/\s*\d{1,4})?(?:\s*(?:min|m))?',re.I)

def parse(value):
    compact=re.sub(r'\s+',' ',value.strip())
    m=STRICT.fullmatch(compact)
    return (m.group('number'),('min' if m.group('qualifier') else None)) if m else (None,None)

def status(kind, strict, canonical=None, uncertain=False):
    if uncertain: return 'NeedsReview'
    if canonical=='ExactMatch' and strict: return 'SafeExact'
    return 'NeedsReview'

def add_occ(occ, raw, page, role, table_type, row, col, bbox, strict, canonical='Unavailable', reason=''):
    number,qual=parse(raw)
    occ.append({'rawText':raw,'normalizedText':number,'qualifier':qual,'page':page,'boundingBox':bbox,'sectionType':role,'tableType':table_type,'row':row,'column':col,'role':role,'confidence':'geometry-bound' if strict else 'OCR-only','canonicalMatch':canonical,'status':status(role,strict,canonical,not strict),'reason':reason or ('Strict grammar + geometry' if strict else 'No strict grammar; retained as occurrence only')})

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output-root',type=Path,required=True); args=ap.parse_args()
    root=args.root; raw=root/'real-output'/'full-award-rapidocr'; tables=root/'real-output'/'table-candidates'; analysis=json.loads((root/'real-output'/'award-page-analysis.json').read_text())
    occurrences=[]; award=[]; courts=[]; classes=[]; claims=[]; narratives=[]; unknown=[]; notifications=[]; identity=[]
    for page in analysis['pages']:
        p=page['page']; concepts=' '.join(page.get('concepts',[])); role='Unknown'; table_type=None
        if 'CWP/court table' in concepts: role='Court/CWP'; table_type='CWP'
        elif 'Award Khasra table' in concepts: role='AwardLandTable'; table_type='AwardKhasraArea'
        elif 'classification' in concepts.lower(): role='LandClassification'; table_type='Classification'
        j=tables/f'page-{p}'/'join.json'
        if j.exists():
            data=json.loads(j.read_text())
            for t in data.get('tables',[]):
                cells=t['table']['cells']; rows={}
                for c in cells: rows.setdefault(c['row'],[]).append(c)
                header=' '.join(str(c.get('text','')) for c in rows.get(0,[])).lower()
                table_role=role; table_kind=table_type
                if 'block' in header and 'khasra' in header:
                    table_role='LandClassification'; table_kind='Classification'
                elif 'status' in header and ('cwp' in header or 'case' in header or 'total area' in header):
                    table_role='Court/CWP'; table_kind='CWP'
                for row,rcells in rows.items():
                    rcells=sorted(rcells,key=lambda c:c['column'])
                    for i,c in enumerate(rcells):
                        cell_text=str(c.get('text','')).strip(); num,qual=parse(cell_text)
                        if not num: continue
                        canonical='Unavailable' # API/DB deliberately offline; no digit repair
                        add_occ(occurrences,cell_text,p,table_role,table_kind,row,c['column'],c.get('bounding_box'),True,canonical)
                        same=[x for x in rcells if x['column']>c['column']]
                        areas=[x for x in same if re.search(r'\d',str(x.get('text','')))]
                        if table_role=='AwardLandTable':
                            rec=areas[0].get('text') if len(areas)>0 else None; aw=areas[1].get('text') if len(areas)>1 else None
                            award.append({'khasra':num,'qualifier':qual,'recordedAreaCandidate':rec,'awardedAreaCandidate':aw,'sourcePage':p,'sourceCells':rcells,'confidence':'geometry-bound','status':'NeedsReview','reason':'Strict Khasra token and row geometry; canonical master validation unavailable and area headers are not promoted without proof'})
                        elif table_role=='Court/CWP':
                            courts.append({'caseNumber':str(rcells[0].get('text','')),'affectedKhasras':[num],'totalArea':None,'exactStatusText':' '.join(str(x.get('text','')) for x in rcells),'date':None,'sourcePage':p,'status':'NeedsReview'})
                        elif table_role=='LandClassification':
                            txt=' '.join(str(x.get('text','')) for x in rcells); block='B' if re.search(r'\bB[- ]?BLOCK\b|\bB\b',txt,re.I) else ('A' if re.search(r'\bA[- ]?BLOCK\b|\bA\b',txt,re.I) else None)
                            classes.append({'khasra':num,'area':None,'classCode':block,'sourcePage':p,'sourceCells':rcells,'status':'NeedsReview','reason':'Block inheritance requires explicit header/row proof'})
        # Every non-strict Khasra-like OCR token is contextual, never a structured candidate.
        raw_path=raw/f'page-{p}.raw.json'
        words=json.loads(raw_path.read_text())
        for w in words:
            txt=str(w.get('txt','')).strip()
            for m in LOOSE.finditer(txt):
                if parse(m.group(0))[0] is None: unknown.append({'rawText':m.group(0),'page':p,'role':role,'reason':'OCR occurrence failed strict grammar'})
        low=' '.join(str(w.get('txt','')) for w in words).lower()
        if p==1:
            for label,pat in [('Award Number',r'award\s*no\.?\s*([^\n]{1,80})'),('Village',r'name\s+of\s+village\s*[:\-]?\s*([^\n]{1,60})'),('Purpose/Nature',r'(?:purpose|nature)\s+of\s+acquisition\s*[:\-]?\s*([^\n]{1,140})')]:
                m=re.search(pat,low,re.I)
                if m: identity.append({'field':label,'rawValue':m.group(1).strip(),'sourcePage':p,'status':'NeedsReview','reason':'OCR identity text; no fuzzy correction'})
            for m in re.finditer(r'notification[^\n]{0,180}',low,re.I):
                notifications.append({'rawText':m.group(0).strip(),'sectionType':'Notification','sourcePage':p,'status':'NeedsReview','reason':'Notification label and source text detected; number/date require exact human confirmation'})
        for label,terms in [('Possession',['possession','dispossession']),('Valuation',['market value','valuation','rate']),('Compensation',['compensation','solatium','interest']),('AreaReconciliation',['corrigendum','field book','excess','shortage','difference']),('Claims',['claim','claimant'])]:
            if any(term in low for term in terms): narratives.append({'concept':label,'page':p,'originalText':' '.join(str(w.get('txt','')) for w in words),'status':'NarrativeOnly','confidence':'OCR text; section structure not sufficient'})
    # Claims remain separate because no claims table geometry was detected.
    claims=[x for x in narratives if x['concept']=='Claims']
    result={'generatedAtUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'mode':'read-only experimental domain draft','canonicalValidation':{'status':'Unavailable','reason':'local API/database was offline; no master data was used to repair OCR'},'occurrenceGraph':{'occurrences':occurrences,'contextualUnknowns':unknown},'structured':{'identity':identity,'awardKhasraRows':award,'courtCases':courts,'classification':classes,'claims':claims,'narrativeFacts':narratives,'notifications':notifications},'summary':{'distinctStrictKhasras':len({(x['normalizedText'],x['qualifier']) for x in occurrences if x['normalizedText']}),'strictKhasraOccurrences':len(occurrences),'structuredAwardRows':len(award),'safeExactRows':0,'needsReviewRows':len(award)+len(courts)+len(classes)+len(claims)+len(narratives)+len(notifications)+len(identity),'conflictRows':0,'unreadableRows':len(unknown),'unknownKhasraOccurrences':len(unknown),'courtCases':len(courts),'classificationLinks':len(classes),'claims':len(claims),'notifications':len(notifications),'canonicalMatchExact':0}}
    args.output_root.mkdir(parents=True,exist_ok=True)
    (args.output_root/'award-occurrence-graph.json').write_text(json.dumps(result['occurrenceGraph'],ensure_ascii=False,indent=2),encoding='utf-8')
    (args.output_root/'award-domain-draft-v2.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(result['summary'],indent=2))
if __name__=='__main__': main()
