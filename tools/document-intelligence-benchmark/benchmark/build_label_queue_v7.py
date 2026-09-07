"""Select varied, difficult non-gold cells for local keyboard-first human labelling."""
import argparse,json,re
from pathlib import Path
from PIL import Image
def page_number(name):
 m=re.fullmatch(r'page-(\d+)',name); return int(m.group(1)) if m else None
def role(col): return 'khasra' if col%3==0 else ('recordedArea' if col%3==1 else 'awardedArea')
def priority(text,r):
 s=1+len(text)/20
 if '/' in text or '-' in text: s+=2
 if 'min' in text: s+=3
 if not text.strip(): s+=4
 return s + (r%7)/100
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--limit',type=int,default=100); a=ap.parse_args(); a.output.mkdir(parents=True,exist_ok=True); gold=json.loads((a.root/'real-output/ocr-gold-manifest.json').read_text()); held={(2,x['row'],x['column']) for x in gold}; all=[]
 for folder in (a.root/'real-output/table-candidates').iterdir():
  p=page_number(folder.name); image_path=a.root/f'real-output/full-award-rapidocr/page-{p}.png' if p else None
  if not p or not image_path.exists() or not (folder/'join.json').exists(): continue
  image=Image.open(image_path).convert('RGB')
  for table in json.loads((folder/'join.json').read_text()).get('tables',[]):
   for c in table['table']['cells']:
    if (p,c['row'],c['column']) in held: continue
    b=c['bounding_box']; pad=10; l=max(0,int(b['x'])-pad); t=max(0,int(b['y'])-pad); rr=min(image.width,int(b['x']+b['width'])+pad); bb=min(image.height,int(b['y']+b['height'])+pad); crop=a.output/f'p{p}-r{c["row"]}-c{c["column"]}.png'; image.crop((l,t,rr,bb)).save(crop); all.append({'id':crop.stem,'role':role(c['column']),'suggestion':c['text'],'sourcePage':p,'sourceRow':c['row'],'sourceColumn':c['column'],'sourceCrop':str(crop),'priority':priority(c['text'],c['row'])})
 # retain varied roles and pages rather than near-identical adjacent cells
 all.sort(key=lambda x:-x['priority']); chosen=[]; seen=set()
 for x in all:
  bucket=(x['role'],x['sourcePage'],x['sourceRow']//3)
  if bucket in seen: continue
  chosen.append(x); seen.add(bucket)
  if len(chosen)>=a.limit: break
 (a.output/'label-queue.json').write_text(json.dumps(chosen,indent=2),encoding='utf-8'); print(json.dumps({'queued':len(chosen),'roles':{k:sum(x['role']==k for x in chosen) for k in set(x['role'] for x in chosen)}}))
if __name__=='__main__': main()
