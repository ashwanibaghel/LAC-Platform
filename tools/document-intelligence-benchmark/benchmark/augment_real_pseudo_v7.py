"""Create modest local variants of strict pseudo-labelled crops."""
import argparse,json,random
from pathlib import Path
from PIL import Image,ImageEnhance,ImageFilter
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--labels',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--variants',type=int,default=5); a=ap.parse_args(); r=random.Random(707); a.output.mkdir(parents=True,exist_ok=True); out=[]
 for item in json.loads(a.labels.read_text()):
  source=item.get('sourceCrop',item.get('image'))
  for n in range(a.variants):
   im=Image.open(source).convert('L'); im=im.resize((max(8,int(im.width*r.uniform(.9,1.1))),max(8,int(im.height*r.uniform(.9,1.1)))),Image.Resampling.LANCZOS); im=ImageEnhance.Contrast(im).enhance(r.uniform(.8,1.25)); im=ImageEnhance.Brightness(im).enhance(r.uniform(.85,1.15));
   if n%2: im=im.filter(ImageFilter.GaussianBlur(.35))
   p=a.output/f'{Path(source).stem}-v{n}.png'; im.save(p); out.append({'image':str(p),'text':item['text'],'role':item['role'],'sourcePage':item['sourcePage'],'sourceRow':item['sourceRow'],'sourceColumn':item['sourceColumn'],'labelSource':item.get('labelSource','pseudo')})
 (a.output/'manifest.json').write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps({'variants':len(out)}))
if __name__=='__main__': main()
