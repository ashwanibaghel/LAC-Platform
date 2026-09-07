"""Score OCR outputs against the local visual gold manifest only."""
import argparse,json,re
from pathlib import Path
def clean(s): return ' '.join(str(s or '').strip().split())
def exact(pred,gold): return clean(pred)==clean(gold)
def structural(field,s):
    s=clean(s).lower().replace('—','-').replace('–','-')
    if field=='khasra':
        m=re.search(r'\d+\s*/+\s*\d+(?:\s*/+\s*\d+)?(?:\s*(?:min|m1n))?',s)
        return re.sub(r'\s+','',m.group(0)) if m else None
    nums=re.findall(r'\d+',s)
    return tuple(nums[:2]) if len(nums)>=2 else None
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--rapid',type=Path,required=True); ap.add_argument('--tesseract',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); r={x['id']:x for x in json.loads(a.rapid.read_text(encoding='utf-8'))['cells']}; t={x['id']:x for x in json.loads(a.tesseract.read_text(encoding='utf-8'))['cells']}; metrics={}
    for engine,data in [('RapidOCR',r),('Tesseract5',t)]:
        for variant in ('original','grayscale'):
            fields={}
            for field in ('khasra','recorded','awarded'):
                rows=[x for x in data.values() if x['field']==field]; good=0; shape=0
                for x in rows:
                    pred=x['predictions']['rapidocr'][variant] if engine=='RapidOCR' else x['predictions']['tesseract'][variant]
                    good += int(exact(pred,x['gold']))
                    shape += int(structural(field,pred)==structural(field,x['gold']))
                fields[field]={'exactCorrect':good,'structuralCorrect':shape,'total':len(rows),'accuracy':round(good/len(rows),4) if rows else None}
            metrics[f'{engine}:{variant}']=fields
    consensus=[]
    for ident,x in r.items():
        if ident not in t: continue
        rp=x['predictions']['rapidocr']['original']; tp=t[ident]['predictions']['tesseract']['original']; consensus.append({'id':ident,'field':x['field'],'gold':x['gold'],'rapidocr':rp,'tesseract':tp,'agreement':exact(rp,tp),'bothCorrect':exact(rp,x['gold']) and exact(tp,x['gold'])})
    rows={}
    for ident,x in r.items():
        key=re.match(r'(r\d+)-',ident).group(1)
        rows.setdefault(key,{})[x['field']]=x
    full={}
    for engine,data in [('RapidOCR',r),('Tesseract5',t)]:
        for variant in ('original','grayscale'):
            exact_rows=shape_rows=0
            for key,items in rows.items():
                vals=[]
                for field in ('khasra','recorded','awarded'):
                    x=items[field]; pred=x['predictions']['rapidocr'][variant] if engine=='RapidOCR' else t[key+'-'+field[0]]['predictions']['tesseract'][variant]
                    vals.append((exact(pred,x['gold']),structural(field,pred)==structural(field,x['gold'])))
                exact_rows+=int(all(v[0] for v in vals)); shape_rows+=int(all(v[1] for v in vals))
            full[f'{engine}:{variant}']={'exactRows':exact_rows,'structuralRows':shape_rows,'totalRows':len(rows)}
    consensus_out={'cells':consensus,'summary':{'cellsCompared':len(consensus),'agreement':sum(x['agreement'] for x in consensus),'bothCorrect':sum(x['bothCorrect'] for x in consensus)},'method':'Agreement only; no master-data voting or canonical matching.'}
    out={'metrics':metrics,'fullRow':full,'consensus':consensus_out['summary'],'note':'Scored against direct visual gold only; no master data used.'}
    a.output.write_text(json.dumps(out,indent=2),encoding='utf-8')
    (a.output.parent/'ocr-consensus-analysis.json').write_text(json.dumps(consensus_out,indent=2),encoding='utf-8')
    print(json.dumps(out,indent=2))
if __name__=='__main__': main()
