"""Run local OCR engines on the same reviewed cell-crop manifest."""
import argparse,json,subprocess,time
from pathlib import Path
from PIL import Image,ImageOps
def norm(s): return ' '.join(str(s or '').strip().split())
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--manifest',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--tesseract',type=Path); ap.add_argument('--fields',nargs='*'); ap.add_argument('--skip-rapidocr',action='store_true'); ap.add_argument('--scale',type=int,default=3); a=ap.parse_args(); items=json.loads(a.manifest.read_text());
    if a.fields: items=[x for x in items if x['field'] in a.fields]
    if not a.skip_rapidocr:
        from rapidocr import EngineType,RapidOCR
        engine=RapidOCR(params={'Det.engine_type':EngineType.TORCH,'Cls.engine_type':EngineType.TORCH,'Rec.engine_type':EngineType.TORCH})
    else: engine=None
    rows=[]; started=time.perf_counter()
    for item in items:
        image=Image.open(item['crop']).convert('RGB')
        if a.scale > 1: image=image.resize((image.width*a.scale,image.height*a.scale),Image.Resampling.LANCZOS)
        variants={'original':image,'grayscale':ImageOps.grayscale(image)}; predictions={'gold':item['gold'],'rapidocr':{},'tesseract':{}}
        for name,img in variants.items():
            if engine: out=engine(img); predictions['rapidocr'][name]=norm(' '.join(str(x) for x in (out.txts or [])))
            if a.tesseract:
                tmp=item['crop']+'.tmp.png'; img.save(tmp); p=subprocess.run([str(a.tesseract),tmp,'stdout','--psm','7'],capture_output=True,text=True,encoding='utf-8',errors='ignore'); predictions['tesseract'][name]=norm(p.stdout); Path(tmp).unlink(missing_ok=True)
        rows.append({'id':item['id'],'field':item['field'],'gold':item['gold'],'predictions':predictions})
    result={'engineRuntimeSeconds':round(time.perf_counter()-started,2),'cells':rows,'engines':['RapidOCR']+(['Tesseract 5'] if a.tesseract else [])}; a.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps({'cells':len(rows),'seconds':result['engineRuntimeSeconds'],'engines':result['engines']},indent=2))
if __name__=='__main__': main()
