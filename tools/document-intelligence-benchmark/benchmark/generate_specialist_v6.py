"""Generate fictional, augmented cell images for the local specialist OCR experiment."""
import argparse,json,random
from pathlib import Path
from PIL import Image,ImageDraw,ImageFont,ImageFilter,ImageEnhance
from .specialist_grammar_v6 import valid_role
ROLES=('rectangle','killa','khasra','area','qualifier')
def value(role,r):
    if role=='rectangle': return str(r.randint(1,199))
    if role=='killa': return f'{r.randint(1,40)}/{r.randint(1,19)}' if r.random()<.35 else str(r.randint(1,40))
    if role=='khasra':
        s=f'{r.randint(1,199)}//{r.randint(1,40)}'
        if r.random()<.3: s+=f'/{r.randint(1,19)}'
        if r.random()<.2: s+=' min'
        return s
    if role=='area': return f'{r.randint(0,149)}-{r.randint(0,19):02d}'
    return 'min'
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--count',type=int,default=3000); ap.add_argument('--seed',type=int,default=606); a=ap.parse_args(); r=random.Random(a.seed); a.output.mkdir(parents=True,exist_ok=True)
    fonts=[Path('C:/Windows/Fonts/arial.ttf'),Path('C:/Windows/Fonts/times.ttf'),Path('C:/Windows/Fonts/calibri.ttf')]; fonts=[x for x in fonts if x.exists()]
    meta=[]
    for i in range(a.count):
        role=ROLES[i%len(ROLES)]; text=value(role,r); font=ImageFont.truetype(str(fonts[i%len(fonts)]),r.randint(22,32)) if fonts else ImageFont.load_default()
        im=Image.new('L',(192,56),255); d=ImageDraw.Draw(im); box=d.textbbox((0,0),text,font=font); x=max(2,(192-(box[2]-box[0]))//2); y=max(1,(56-(box[3]-box[1]))//2-3); d.text((x,y),text,fill=r.randint(0,60),font=font)
        if r.random()<.45: im=im.rotate(r.uniform(-2.0,2.0),fillcolor=255,expand=False)
        if r.random()<.5: im=im.filter(ImageFilter.GaussianBlur(r.uniform(.15,.7)))
        if r.random()<.4: im=ImageEnhance.Contrast(im).enhance(r.uniform(.55,1.2))
        path=a.output/f'{i:05d}.png'; im.save(path); meta.append({'image':str(path),'role':role,'text':text})
    (a.output/'manifest.json').write_text(json.dumps(meta,indent=2),encoding='utf-8'); print(json.dumps({'samples':len(meta),'roles':ROLES}))
if __name__=='__main__': main()
