"""Fine-tune docTR's compact pretrained CRNN locally, with real-label emphasis."""
import argparse,json,random,time
from pathlib import Path
import numpy as np
from PIL import Image
import torch
from doctr.models import recognition_predictor
from .training_guard_v8 import assert_no_gold_leak
def batches(items,size):
 for i in range(0,len(items),size): yield items[i:i+size]
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--manifests',type=Path,nargs='+',required=True); ap.add_argument('--gold',type=Path,required=True); ap.add_argument('--checkpoint',type=Path,required=True); ap.add_argument('--epochs',type=int,default=3); a=ap.parse_args(); items=[]
 for p in a.manifests: items+=json.loads(p.read_text())
 assert_no_gold_leak(items,a.gold)
 # Human corrections carry 3× sampling weight; pseudo labels remain useful but cannot drown them.
 weighted=[x for x in items for _ in range(3 if x.get('labelSource')=='human' else 1)]; random.Random(808).shuffle(weighted); predictor=recognition_predictor('crnn_mobilenet_v3_large',pretrained=True); model=predictor.model; model.train(); opt=torch.optim.Adam(model.parameters(),lr=2e-5); started=time.perf_counter()
 for epoch in range(a.epochs):
  random.shuffle(weighted); losses=[]
  for batch in batches(weighted,16):
   images=[np.array(Image.open(x['image']).convert('RGB')) for x in batch]; tensors=predictor.pre_processor(images)[0]; loss=model(tensors,target=[x['text'] for x in batch])['loss']; opt.zero_grad(); loss.backward(); opt.step(); losses.append(float(loss.detach()))
  print(json.dumps({'epoch':epoch+1,'loss':round(sum(losses)/len(losses),4)}))
 a.checkpoint.parent.mkdir(parents=True,exist_ok=True); torch.save({'state':model.state_dict(),'runtimeSeconds':round(time.perf_counter()-started,2),'samples':len(items),'weightedSamples':len(weighted),'architecture':'docTR crnn_mobilenet_v3_large'},a.checkpoint)
if __name__=='__main__': main()
