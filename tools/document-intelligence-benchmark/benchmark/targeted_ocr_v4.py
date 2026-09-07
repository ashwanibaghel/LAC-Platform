"""Re-OCR only uncertain slash-bearing table cells; preserve every alternative."""
from __future__ import annotations
import argparse,json,re,time
from pathlib import Path
from PIL import Image,ImageEnhance,ImageFilter,ImageOps

STRICT=re.compile(r'^[1-9]\d{0,2}//[1-9]\d{0,2}(?:/[1-9]\d{0,2})*(?:\s*min)?$',re.I)
def exact_consensus(alternatives):
    texts=[t for item in alternatives for t in item.get('texts',[]) if t]
    return texts[0] if texts and all(t==texts[0] for t in texts) else None

def variants(image):
    gray=ImageOps.grayscale(image)
    sharp=ImageEnhance.Sharpness(gray).enhance(2.5)
    contrast=ImageEnhance.Contrast(sharp).enhance(1.8)
    threshold=contrast.point(lambda p: 255 if p>175 else 0)
    return [('gray',gray),('sharp',sharp),('threshold',threshold)]

def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--limit',type=int,default=30); args=ap.parse_args()
    from rapidocr import EngineType,RapidOCR
    rawroot=args.root/'real-output'/'full-award-rapidocr'; tabroot=args.root/'real-output'/'table-candidates'; engine=RapidOCR(params={'Det.engine_type':EngineType.TORCH,'Cls.engine_type':EngineType.TORCH,'Rec.engine_type':EngineType.TORCH})
    seen=set(); results=[]; started=time.perf_counter()
    for page_dir in sorted(tabroot.glob('page-*'),key=lambda p:int(p.name.split('-')[1])):
        join=page_dir/'join.json'; png=rawroot/(page_dir.name+'.png')
        if not join.exists() or not png.exists(): continue
        data=json.loads(join.read_text()); page=int(page_dir.name.split('-')[1]); source=Image.open(png).convert('RGB')
        for table in data.get('tables',[]):
            for cell in table['table']['cells']:
                text=str(cell.get('text','')).strip()
                if '/' not in text or STRICT.fullmatch(text): continue
                b=cell['bounding_box']; key=(page,table['table_index'],cell['row'],cell['column'],text)
                if key in seen: continue
                seen.add(key); pad=10; left=max(0,int(b['x'])-pad); top=max(0,int(b['y'])-pad); right=min(source.width,int(b['x']+b['width'])+pad); bottom=min(source.height,int(b['y']+b['height'])+pad)
                crop=source.crop((left,top,right,bottom)).resize(((right-left)*3,(bottom-top)*3))
                alternatives=[]
                for name,img in variants(crop):
                    out=engine(img); txts=[str(x).strip() for x in (out.txts or []) if str(x).strip()]
                    alternatives.append({'variant':name,'texts':txts})
                consensus=exact_consensus(alternatives)
                variant_consensus=bool(consensus and consensus!=text and STRICT.fullmatch(consensus))
                # These are preprocessing variants of the same RapidOCR engine,
                # not independent engines; keep consensus visible but do not
                # promote it to an accepted recovery or SafeExact value.
                results.append({'page':page,'tableIndex':table['table_index'],'row':cell['row'],'column':cell['column'],'rawPrimaryText':text,'sourceCell':cell,'preprocessingVariantResults':alternatives,'consensusText':consensus,'consensusReason':'All local preprocessing variants agreed exactly' if consensus else 'Variants disagree or produced no text','variantConsensus':variant_consensus,'acceptedRecovery':False,'acceptanceReason':'Independent OCR engine agreement unavailable; retained as NeedsReview'})
                if len(results)>=args.limit: break
            if len(results)>=args.limit: break
        if len(results)>=args.limit: break
    args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(json.dumps({'elapsedSeconds':round(time.perf_counter()-started,2),'cellsProcessed':len(results),'acceptedRecoveries':sum(x['acceptedRecovery'] for x in results),'results':results},ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps({'cellsProcessed':len(results),'acceptedRecoveries':sum(x['acceptedRecovery'] for x in results),'elapsedSeconds':round(time.perf_counter()-started,2)},indent=2))
if __name__=='__main__': main()
