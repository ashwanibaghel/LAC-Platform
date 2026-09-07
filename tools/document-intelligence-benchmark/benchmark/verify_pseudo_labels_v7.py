"""Require independent local OCR preprocessing agreement before pseudo-label acceptance."""
import argparse,json
from pathlib import Path
from PIL import Image,ImageOps
from rapidocr import EngineType,RapidOCR
from .specialist_grammar_v6 import valid_khasra
def text(out): return ' '.join(str(x) for x in (out.txts or [])).strip()
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--labels',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); engine=RapidOCR(params={'Det.engine_type':EngineType.TORCH,'Cls.engine_type':EngineType.TORCH,'Rec.engine_type':EngineType.TORCH}); ok=[]; rejected=[]
 for item in json.loads(a.labels.read_text()):
  im=Image.open(item['sourceCrop']).convert('RGB').resize((Image.open(item['sourceCrop']).width*3,Image.open(item['sourceCrop']).height*3),Image.Resampling.LANCZOS); o=text(engine(im)); g=text(engine(ImageOps.grayscale(im)))
  if o==item['rawOcr'] and g==item['rawOcr'] and valid_khasra(o): ok.append({**item,'verification':{'original':o,'grayscale':g,'agreed':True}})
  else: rejected.append({'sourceCrop':item['sourceCrop'],'rawOcr':item['rawOcr'],'original':o,'grayscale':g,'reason':'preprocessing-disagreement-or-nonexact'})
 a.output.write_text(json.dumps(ok,indent=2),encoding='utf-8'); (a.output.parent/'pseudo-verification-rejected.json').write_text(json.dumps(rejected,indent=2),encoding='utf-8'); print(json.dumps({'accepted':len(ok),'rejected':len(rejected)}))
if __name__=='__main__': main()
