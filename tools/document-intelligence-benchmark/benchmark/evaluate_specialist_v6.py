"""Evaluate the synthetic-only CRNN against held-out real gold crops."""
import argparse,json,re
from pathlib import Path
import torch
from PIL import Image
import numpy as np
from .train_specialist_v6 import CRNN
from .specialist_grammar_v6 import valid_role
def decode(logits,chars):
 ids=logits.argmax(-1).cpu().tolist(); out=[]
 for row in zip(*ids):
  s=''; last=0
  for i in row:
   if i and i!=last: s+=chars[i-1]
   last=i
  out.append(s)
 return out
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--checkpoint',type=Path,required=True); ap.add_argument('--manifest',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); ck=torch.load(a.checkpoint,map_location='cpu'); model=CRNN(); model.load_state_dict(ck['state']); model.eval(); rows=[]
 with torch.no_grad():
  for x in json.loads(a.manifest.read_text()):
   role='khasra' if x['field']=='khasra' else 'area'; im=Image.open(x['crop']).convert('L').resize((192,56)); t=torch.from_numpy(np.array(im,dtype='float32')[None,None]/255.); logits=model(t); prob=logits.softmax(-1).max(-1).values.mean().item(); pred=decode(logits,ck['chars'])[0]; rows.append({'id':x['id'],'field':x['field'],'gold':x['gold'],'recognizedText':pred,'confidence':round(prob,4),'topAlternatives':[],'cellRole':role,'grammarValid':valid_role(role,pred),'sourceCrop':x['crop']})
 grouped={}
 for x in rows: grouped.setdefault(re.match(r'(r\d+)',x['id']).group(1),{})[x['field']]=x
 exact={f:sum(x['recognizedText']==x['gold'] for x in rows if x['field']==f) for f in ('khasra','recorded','awarded')}; full=sum(all(g[f]['recognizedText']==g[f]['gold'] for f in ('khasra','recorded','awarded')) for g in grouped.values()); safe=[k for k,g in grouped.items() if all(g[f]['recognizedText']==g[f]['gold'] and g[f]['grammarValid'] for f in ('khasra','recorded','awarded'))]
 out={'training':{'samples':ck['trainSamples'],'runtimeSeconds':ck['runtimeSeconds'],'device':'CPU'},'metrics':{'exact':{k:{'correct':v,'total':25,'accuracy':round(v/25,4)} for k,v in exact.items()},'fullRow':{'correct':full,'total':25,'accuracy':round(full/25,4)},'separatorAccuracy':sum('--' in x['recognizedText'] for x in rows if x['field']!='khasra')/50},'safeExact':{'count':len(safe),'correct':len(safe),'precision':1.0 if safe else None,'rows':safe},'cells':rows}; a.output.write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps(out['metrics'],indent=2))
if __name__=='__main__': main()
