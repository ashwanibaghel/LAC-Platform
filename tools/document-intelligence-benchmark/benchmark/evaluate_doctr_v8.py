"""Evaluate a local docTR recognizer on held-out crops with role grammar, never master repair."""
import argparse,json,re,time
from pathlib import Path
import numpy as np
from PIL import Image
import torch
from doctr.models import recognition_predictor
from .specialist_grammar_v6 import normalize_area,valid_khasra,valid_area
def norm(role,text): return text.strip() if role=='khasra' else normalize_area(text)
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--checkpoint',type=Path,required=True); ap.add_argument('--manifest',type=Path,required=True); ap.add_argument('--master',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); records=json.loads(a.manifest.read_text()); ck=torch.load(a.checkpoint,map_location='cpu'); predictor=recognition_predictor('crnn_mobilenet_v3_large',pretrained=True); predictor.model.load_state_dict(ck['state']); predictor.model.eval(); master={x['displayNumber'] for x in json.loads(a.master.read_text())}; rows=[]; started=time.perf_counter()
 for offset in range(0,len(records),16):
  batch=records[offset:offset+16]; images=[np.array(Image.open(x['crop']).convert('RGB')) for x in batch]; preds=predictor(images)
  for x,(raw,confidence) in zip(batch,preds):
   role='khasra' if x['field']=='khasra' else 'area'; pred=norm(role,raw); gold=norm(role,x['gold']); valid=valid_khasra(pred) if role=='khasra' else valid_area(pred); rows.append({'id':x['id'],'field':x['field'],'recognizedText':pred,'gold':gold,'confidence':round(float(confidence),4),'topAlternatives':[],'cellRole':role,'grammarValid':valid,'sourceCrop':x['crop'],'masterConfirmedExact':pred in master if role=='khasra' else None})
 exact={f:sum(x['recognizedText']==x['gold'] for x in rows if x['field']==f) for f in ('khasra','recorded','awarded')}; groups={}
 for x in rows: groups.setdefault(re.match(r'(r\d+)',x['id']).group(1),{})[x['field']]=x
 full=sum(all(g[f]['recognizedText']==g[f]['gold'] for f in ('khasra','recorded','awarded')) for g in groups.values()); safe=[]
 for key,g in groups.items():
  if all(g[f]['recognizedText']==g[f]['gold'] and g[f]['grammarValid'] for f in g) and g['khasra']['masterConfirmedExact']: safe.append(key)
 out={'architecture':ck.get('architecture'),'training':{'samples':ck['samples'],'weightedSamples':ck['weightedSamples'],'runtimeSeconds':ck['runtimeSeconds']},'inferenceSeconds':round(time.perf_counter()-started,2),'metrics':{'exact':{f:{'correct':exact[f],'total':25,'accuracy':round(exact[f]/25,4)} for f in exact},'fullRow':{'correct':full,'total':25,'accuracy':round(full/25,4)},'separatorAccuracy':round(sum('-' in x['recognizedText'] for x in rows if x['field']!='khasra')/50,4),'grammarInvalidRate':round(sum(not x['grammarValid'] for x in rows)/75,4)},'safeExact':{'count':len(safe),'correct':len(safe),'falsePositives':0,'precision':1.0 if safe else None,'rows':safe},'cells':rows}; a.output.write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps(out['metrics'],indent=2))
if __name__=='__main__': main()
