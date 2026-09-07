"""Phase 3: role-aware Award validation against a read-only local master export."""
from __future__ import annotations
import argparse,json,re,time
from pathlib import Path

TOKEN=re.compile(r'(?<![A-Za-z0-9])(?P<n>[1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*)(?:\s*(?P<q>min))?(?![A-Za-z0-9])',re.I)
SLASH=re.compile(r'(?<![A-Za-z0-9])\d{1,3}(?:/|//)\d{1,3}(?:/\d{1,3})?(?:\s*(?:min|m))?',re.I)
AREA=re.compile(r'^\s*\d{1,4}\s*(?:--?|–|—)\s*\d{1,2}(?:\s*(?:[-,]\s*\d{1,2}))?\s*$',re.I)

def norm(s): return re.sub(r'\s+','',str(s).upper()).replace('MIN','')
def parse(s):
    m=TOKEN.fullmatch(re.sub(r'\s+',' ',str(s).strip()))
    return (m.group('n'),('min' if m.group('q') else None)) if m else (None,None)
def reconstruct(rectangle, killa, qualifier=None):
    """Combine separate rectangle/killa cells only when both are explicit."""
    if not re.fullmatch(r'[1-9]\d{0,2}',str(rectangle).strip()): return (None,'Unreadable')
    k=str(killa).strip(); q=' min' if str(qualifier).strip().lower()=='min' else ''
    if not re.fullmatch(r'[1-9]\d{0,2}(?:/[1-9]\d{0,2})*',k): return (None,'Unreadable')
    return (f'{str(rectangle).strip()}//{k}{q}','ExplicitCells')
def tokens(s):
    return [(m.group(0).strip(),parse(m.group(0))) for m in SLASH.finditer(str(s))]
def match_master(number,q,master):
    key=norm(number); exact=[x for x in master if norm(x['displayNumber'].replace(' min',''))==key and (('min' in x['displayNumber'].lower())==(q=='min'))]
    if exact:return ('ExactMasterMatch',exact[0])
    stem=[x for x in master if norm(x['displayNumber'].replace(' min',''))==key]
    if stem:return ('QualifierMismatch',stem[0])
    return ('NoMasterMatch',None)
def evidence(c): return {'row':c.get('row'),'column':c.get('column'),'boundingBox':c.get('bounding_box'),'text':c.get('text')}

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--master',type=Path,required=True); ap.add_argument('--output-root',type=Path,required=True); args=ap.parse_args()
    root=args.root; master=json.loads(args.master.read_text()); joins=root/'real-output'/'table-candidates'; analysis=json.loads((root/'real-output'/'award-page-analysis.json').read_text())
    occ=[]; rows=[]; cwp=[]; classification=[]; seen=set(); unknown=[]; master_validation=[]
    for pinfo in analysis['pages']:
        p=pinfo['page']; concepts=' '.join(pinfo.get('concepts',[])); default='Court/CWP' if 'CWP/court table' in concepts else ('AwardLandTable' if 'Award Khasra table' in concepts else 'Unknown')
        raw_words=json.loads((root/'real-output'/'full-award-rapidocr'/f'page-{p}.raw.json').read_text())
        page_text=' '.join(str(w.get('txt','')) for w in raw_words).lower()
        award_header_proven=bool(re.search(r'rec\s*no.*khasra.*total\s*area.*area\s*awarded',page_text,re.I))
        j=joins/f'page-{p}'/'join.json'
        if not j.exists(): continue
        data=json.loads(j.read_text())
        for table in data.get('tables',[]):
            cells=table['table']['cells']; byrow={}
            for c in cells: byrow.setdefault(c['row'],[]).append(c)
            header=' '.join(str(c.get('text','')) for c in byrow.get(0,[])).lower()
            role='LandClassification' if ('block' in header and 'khasra' in header) else ('Court/CWP' if ('status' in header and ('case' in header or 'total area' in header)) else default)
            # Infer Khasra/Killa columns from slash-bearing cells, excluding area-shaped cells.
            colscore={}
            for c in cells:
                txt=str(c.get('text','')).strip()
                if '/' in txt and not AREA.fullmatch(txt): colscore[c['column']]=colscore.get(c['column'],0)+1
            khasra_cols={col for col,n in colscore.items() if n>=1}
            for row,rcells in byrow.items():
                rcells=sorted(rcells,key=lambda c:c['column']); bycol={c['column']:c for c in rcells}
                for col in sorted(khasra_cols):
                    c=bycol.get(col)
                    if not c: continue
                    raw=str(c.get('text','')).strip()
                    found=tokens(raw)
                    if not found:
                        if '/' in raw: unknown.append({'page':p,'role':role,'rawText':raw,'sourceCell':evidence(c),'reason':'slash-bearing OCR cell failed strict grammar'})
                        continue
                    for raw_token,(number,q) in found:
                        if number is None:
                            unknown.append({'page':p,'role':role,'rawText':raw_token,'sourceCell':evidence(c),'reason':'slash-bearing OCR token failed strict grammar; no digit repair'})
                            continue
                        key=(p,table['table_index'],row,col,raw_token,role)
                        if key in seen: continue
                        seen.add(key); vm,record=match_master(number,q,master)
                        item={'rawText':raw_token,'normalizedText':number,'qualifier':q,'page':p,'role':role,'tableIndex':table['table_index'],'row':row,'column':col,'sourceCell':evidence(c),'canonicalMatch':vm,'canonicalKhasraId':record.get('id') if record else None,'status':'NeedsReview','reason':'Master validates only; no digit repair'}
                        occ.append(item); master_validation.append({k:item[k] for k in ['rawText','normalizedText','qualifier','page','role','canonicalMatch','canonicalKhasraId']})
                        if role=='AwardLandTable':
                            right=[bycol[x] for x in sorted(bycol) if x>col]
                            areas=[x for x in right if AREA.fullmatch(str(x.get('text','')).strip())]
                            rows.append({'khasra':number,'qualifier':q,'recordedArea':str(areas[0].get('text')).strip() if len(areas)>0 else None,'awardedArea':str(areas[1].get('text')).strip() if len(areas)>1 else None,'areaSemantics':'ProvenByPageHeader' if award_header_proven else 'NeedsReview — area header not detected','sourcePage':p,'sourceCells':[evidence(x) for x in rcells],'canonicalMatch':vm,'canonicalKhasraId':record.get('id') if record else None,'status':'NeedsReview'})
                        elif role=='Court/CWP':
                            cwp.append({'caseNumber':str(rcells[1].get('text','')) if len(rcells)>1 else None,'affectedKhasras':[number],'totalArea':str(rcells[3].get('text','')) if len(rcells)>3 else None,'exactStatusText':' '.join(str(x.get('text','')) for x in rcells),'sourcePage':p,'canonicalMatch':vm,'status':'NeedsReview'})
                        elif role=='LandClassification':
                            blocks=[x for x in rcells if str(x.get('text','')).strip().lower() not in ('','-do-','-d0-') and re.fullmatch(r'[ab]',str(x.get('text','')).strip(),re.I)]
                            classification.append({'khasra':number,'area':None,'classCode':str(blocks[0].get('text')).strip().upper() if blocks else None,'sourcePage':p,'sourceCells':[evidence(x) for x in rcells],'canonicalMatch':vm,'status':'NeedsReview'})
    distinct={norm(x['normalizedText'])+(' min' if x['qualifier']=='min' else '') for x in occ}
    counts={k:sum(1 for x in master_validation if x['canonicalMatch']==k) for k in ('ExactMasterMatch','QualifierMismatch','NoMasterMatch','Ambiguous','Unreadable')}
    summary={'masterVillage':'POCHAN PUR','canonicalKhasrasAvailable':len(master),'qualifiersPresent':sum('min' in x['displayNumber'].lower() for x in master),'awardRowsAfterColumnRole':len(rows),'technicalDuplicateRowsCollapsed':0,'distinctReconstructedKhasras':len(distinct),'strictOccurrences':len(occ),'exactMasterMatches':counts['ExactMasterMatch'],'qualifierMismatches':counts['QualifierMismatch'],'noMasterMatches':counts['NoMasterMatch'],'unreadable':len(unknown),'safeExact':0,'needsReview':len(rows)+len(cwp)+len(classification),'conflicts':0,'cwpCases':len({(x['sourcePage'],x['caseNumber']) for x in cwp}),'cwpLinks':len(cwp),'classificationLinks':len(classification),'unknownSlashCells':len(unknown),'claims':'NeedsReview/NarrativeOnly','canonicalWrites':0}
    result={'generatedAtUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'mode':'read-only Phase 3 validation','summary':summary,'structured':{'awardRows':rows,'cwpCases':cwp,'classification':classification},'occurrences':occ,'masterValidation':master_validation,'unknown':unknown,'rules':{'columnRoleRequired':True,'masterNeverRepairsDigits':True,'areaSemantics':'Award recorded total area and awarded area remain separate; no Village Master area copied','safeExactRequiresAllGates':True}}
    args.output_root.mkdir(parents=True,exist_ok=True); (args.output_root/'award-domain-draft-v3.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8'); (args.output_root/'award-master-validation.json').write_text(json.dumps({'village':'POCHAN PUR','validation':master_validation,'summary':summary},ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps(summary,indent=2))
if __name__=='__main__': main()
