"""Train a tiny CPU CRNN/CTC recognizer on fictional cells only."""
import argparse,json,random,time
from pathlib import Path
import torch
from torch import nn
from torch.utils.data import Dataset,DataLoader
from PIL import Image
import numpy as np
CHARS=''.join(dict.fromkeys('0123456789/- min')); IDX={c:i+1 for i,c in enumerate(CHARS)}
class Cells(Dataset):
 def __init__(self,items): self.items=items
 def __len__(self): return len(self.items)
 def __getitem__(self,i): return torch.from_numpy(np.array(Image.open(self.items[i]['image']).convert('L'),dtype='float32')[None]/255.),self.items[i]['text'],self.items[i]['role']
def collate(batch):
 xs,txt,roles=zip(*batch); return torch.stack(xs),txt,roles
class CRNN(nn.Module):
 def __init__(self):
  super().__init__(); self.cnn=nn.Sequential(nn.Conv2d(1,32,3,padding=1),nn.ReLU(),nn.MaxPool2d(2),nn.Conv2d(32,64,3,padding=1),nn.ReLU(),nn.MaxPool2d(2),nn.Conv2d(64,96,3,padding=1),nn.ReLU(),nn.MaxPool2d((2,1))); self.rnn=nn.LSTM(96*7,128,2,bidirectional=True); self.fc=nn.Linear(256,len(CHARS)+1)
 def forward(self,x):
  z=self.cnn(x).permute(3,0,1,2).flatten(2); z,_=self.rnn(z); return self.fc(z)
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--manifest',type=Path,required=True); ap.add_argument('--checkpoint',type=Path,required=True); ap.add_argument('--epochs',type=int,default=3); ap.add_argument('--batch',type=int,default=64); a=ap.parse_args(); items=json.loads(a.manifest.read_text()); random.Random(9).shuffle(items); train=Cells(items[:int(len(items)*.9)]); loader=DataLoader(train,batch_size=a.batch,shuffle=True,collate_fn=collate); model=CRNN(); opt=torch.optim.Adam(model.parameters(),lr=2e-3); lossfn=nn.CTCLoss(blank=0,zero_infinity=True); started=time.perf_counter(); model.train()
 for epoch in range(a.epochs):
  total=0
  for x,text,_ in loader:
   y=model(x); targets=torch.tensor([IDX[c] for s in text for c in s],dtype=torch.long); lens=torch.tensor([len(s) for s in text]); ilens=torch.full((len(text),),y.size(0),dtype=torch.long); loss=lossfn(y.log_softmax(2),targets,ilens,lens); opt.zero_grad(); loss.backward(); opt.step(); total+=float(loss)
  print(json.dumps({'epoch':epoch+1,'loss':round(total/max(1,len(loader)),4)}))
 a.checkpoint.parent.mkdir(parents=True,exist_ok=True); torch.save({'state':model.state_dict(),'chars':CHARS,'runtimeSeconds':round(time.perf_counter()-started,2),'trainSamples':len(train)},a.checkpoint)
if __name__=='__main__': main()
