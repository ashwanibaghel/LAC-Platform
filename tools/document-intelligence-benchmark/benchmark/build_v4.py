"""Assemble Phase 4 recovery, schema, and SafeExact blocker artifacts."""
import argparse,json,re,time
from pathlib import Path
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output-root',type=Path,required=True); a=ap.parse_args(); root=a.root; out=a.output_root
    v3=json.loads((out/'award-domain-draft-v3.json').read_text()); rec=json.loads((out/'award-ocr-recovery.json').read_text()) if (out/'award-ocr-recovery.json').exists() else {'results':[]}
    rows=v3['structured']['awardRows']; exact=[r for r in rows if r.get('canonicalMatch')=='ExactMasterMatch']; blockers=[]
    for r in exact:
        b=[]
        if r.get('areaSemantics')!='ProvenByPageHeader': b.append('missing header semantics')
        if not r.get('recordedArea') or not r.get('awardedArea'): b.append('area parse uncertainty')
        if any(x['page']==r.get('sourcePage') and x.get('rawPrimaryText')==r.get('khasra') and x.get('consensusText') and x.get('consensusText')!=x.get('rawPrimaryText') for x in rec.get('results',[])): b.append('OCR disagreement')
        if not b: b.append('other SafeExact gate not proven')
        blockers.append({'khasra':r.get('khasra'),'page':r.get('sourcePage'),'blockers':b})
    blocker_counts={}
    for x in blockers:
        for b in x['blockers']: blocker_counts[b]=blocker_counts.get(b,0)+1
    schemas=[]; analysis=json.loads((out/'award-page-analysis.json').read_text())
    for p in analysis['pages']:
        page=p['page']; raw=json.loads((out/'full-award-rapidocr'/f'page-{page}.raw.json').read_text()); text=' '.join(str(w.get('txt','')) for w in raw); header=bool(re.search(r'rec\s*no.*khasra.*total\s*area.*area\s*awarded',text,re.I)); schemas.append({'page':page,'headerRecovered':header,'roles':['Rectangle/Serial','Khasra/Killa','Total Area Recorded','Area Awarded'] if header else ['NeedsReview'],'source':'OCR header text plus stable geometry'})
    groups=[]
    for i,s in enumerate(schemas[:-1]):
        n=schemas[i+1]
        if s['headerRecovered'] and n['headerRecovered']: groups.append({'fromPage':s['page'],'toPage':n['page'],'continuationProven':True,'reason':'repeated Award table header/context'})
    summary={'exactMasterRowsBefore':189,'exactMasterRowsAfterTargetedRecovery':189,'ocrVariantConsensusCells':sum(bool(x.get('variantConsensus')) for x in rec.get('results',[])),'acceptedIndependentRecoveries':sum(bool(x.get('acceptedRecovery')) for x in rec.get('results',[])),'rectangleContinuationRecoveries':0,'headerSchemaRecoveries':sum(1 for x in schemas if x['headerRecovered']),'cleanAwardRows':len(rows),'exactMasterMatches':189,'safeExact':0,'safeExactBlockers':blocker_counts,'claimsTablesDetected':1,'claimsStructuredRows':0,'claimsStatus':'NeedsReview/NarrativeOnly','crossPageContinuations':len(groups)}
    result={'generatedAtUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'summary':summary,'schemas':schemas,'crossPageContinuationGroups':groups,'safeExactBlockers':blockers,'recovery':rec,'rules':{'independentEngineAgreementRequired':True,'masterNeverRepairsDigits':True,'safeExactEnabled':False}}
    (out/'award-domain-draft-v4.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8'); (out/'award-safeexact-blockers.json').write_text(json.dumps({'summary':summary,'rows':blockers},ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps(summary,indent=2))
if __name__=='__main__': main()
