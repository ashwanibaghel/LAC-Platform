"""Local fixed-view RapidOCR agreement experiment. No value repair is performed."""
import argparse, json, re, time
from collections import Counter, defaultdict
from pathlib import Path
from PIL import Image, ImageOps
from rapidocr import EngineType, RapidOCR
from .cell_crop_pipeline_v9 import normalize_from_page, normalize_cell_crop
from .specialist_grammar_v6 import normalize_area, valid_area, valid_khasra

VIEWS=("canonical","largerPadding","upscale2","contrastUpscale")
def clean(v): return ' '.join(str(v or '').strip().split())
def semantic(role,v): return clean(v) if role=='khasra' else normalize_area(v)
def valid(role,v): return valid_khasra(v) if role=='khasra' else valid_area(v)
def agreement(role,predictions):
 """Classify same-engine multi-view agreement without choosing by master data."""
 usable=[p for p in predictions if valid(role,p['normalizedPrediction'])]
 if not usable: return 'Unreadable',None,0
 counts=Counter(p['normalizedPrediction'] for p in usable); value,n=counts.most_common(1)[0]
 competitors=len(counts)>1
 total=len(predictions)
 if n==total: return 'StrongAgreement',value,n
 if n>=3 and not competitors: return 'StrongAgreement',value,n
 if competitors: return ('Disagreement' if n==1 else 'ModerateAgreement'),value,n
 return 'ModerateAgreement',value,n
def run_view(engine,image):
 out=engine(image); raw=clean(' '.join(str(x) for x in (out.txts or []))); scores=list(out.scores or [])
 return raw, (sum(float(x) for x in scores)/len(scores) if scores else None)
def page_image(root,page,cache):
 if page not in cache: cache[page]=Image.open(root/f'real-output/full-award-rapidocr/page-{page}.png').convert('RGB')
 return cache[page]
def images_for(root,item,cache):
 canonical=Image.open(item['normalizedCrop']).convert('RGB'); raw=Image.open(item['rawCrop']).convert('RGB')
 box=dict(item['preprocessing']['rawBox']); box.pop('cropBox',None)
 grown={**box,'x':box['x']-4,'y':box['y']-3,'width':box['width']+8,'height':box['height']+6}
 _,large,_=normalize_from_page(page_image(root,item['sourcePage'],cache),grown)
 contrast=ImageOps.autocontrast(ImageOps.grayscale(raw),cutoff=1).resize((raw.width*2,raw.height*2),Image.Resampling.LANCZOS).convert('RGB')
 return {'canonical':canonical,'largerPadding':large,'upscale2':canonical.resize((canonical.width*2,canonical.height*2),Image.Resampling.LANCZOS),'contrastUpscale':contrast}
def gate_report(records,pool):
 selected=[x for x in records if x['pool']==pool]; out={}
 for level in ('StrongAgreement','ModerateAgreement'):
  for role in ('khasra','area'):
   items=[x for x in selected if x['role']==role and x['agreement']['level']==level]
   out[f'{role}:{level}']={'coverage':len(items),'correct':sum(x['semanticExact'] for x in items),'incorrect':sum(not x['semanticExact'] for x in items),'precision':(sum(x['semanticExact'] for x in items)/len(items) if items else None)}
 return out
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--manifests',type=Path,required=True); ap.add_argument('--master',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--pools',nargs='*',choices=['gold','human'],default=['gold','human']); a=ap.parse_args(); a.output.parent.mkdir(parents=True,exist_ok=True); data=json.loads(a.manifests.read_text()); master={x['displayNumber'] for x in json.loads(a.master.read_text())}; engine=RapidOCR(params={'Det.engine_type':EngineType.TORCH,'Cls.engine_type':EngineType.TORCH,'Rec.engine_type':EngineType.TORCH}); cache={}; records=[]; started=time.perf_counter()
 for pool,key in (('Gold-75','gold'),('HumanLabel-50','human')):
  if key not in a.pools: continue
  for item in data.get(key,[]):
   role=item['role']; predictions=[]
   for view,image in images_for(a.root,item,cache).items():
    raw,confidence=run_view(engine,image); predictions.append({'view':view,'rawPrediction':raw,'normalizedPrediction':semantic(role,raw),'confidence':confidence})
   gold=item.get('gold',item.get('text')); level,value,count=agreement(role,predictions); record={'id':item['id'],'pool':pool,'field':item.get('field',role),'role':role,'sourcePage':item['sourcePage'],'sourceRow':item['sourceRow'],'sourceColumn':item['sourceColumn'],'sourceCellsPreserved':bool(item.get('rawCrop') and item.get('normalizedCrop')),'predictions':predictions,'agreement':{'level':level,'value':value,'agreeingViews':count},'masterExact':(value in master if role=='khasra' and value else None),'semanticExact':(semantic(role,value)==semantic(role,gold) if value else False),'gold':gold}; records.append(record)
 pools={p:gate_report(records,p) for p in ('Gold-75','HumanLabel-50') if any(x['pool']==p for x in records)}; pools['Combined']=gate_report(records,'Gold-75')
 # Add pooled combined counts without treating human areas as recorded/awarded.
 for gate in list(pools['Combined']):
  members=[x for x in records if x['role']==gate.split(':')[0] and x['agreement']['level']==gate.split(':')[1]]; pools['Combined'][gate]={'coverage':len(members),'correct':sum(x['semanticExact'] for x in members),'incorrect':sum(not x['semanticExact'] for x in members),'precision':sum(x['semanticExact'] for x in members)/len(members) if members else None}
 out={'strategy':'MultiViewAgreement (same RapidOCR recognizer, not independent-engine consensus)','views':list(VIEWS),'runtimeSeconds':round(time.perf_counter()-started,2),'pools':pools,'records':records}; a.output.write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps({'runtimeSeconds':out['runtimeSeconds'],'pools':pools},indent=2))
if __name__=='__main__': main()
