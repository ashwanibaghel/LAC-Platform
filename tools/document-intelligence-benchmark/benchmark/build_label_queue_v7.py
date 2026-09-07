"""Select varied, difficult non-gold cells for local keyboard-first human labelling."""
import argparse,json,re
from pathlib import Path
from PIL import Image
def page_number(name):
 m=re.fullmatch(r'page-(\d+)',name); return int(m.group(1)) if m else None
def role(col): return 'khasra' if col%3==0 else ('recordedArea' if col%3==1 else 'awardedArea')
def readable_single_cell(text,cell_role,b):
 text=' '.join(text.split())
 if not text or len(text)>16 or ',' in text or ';' in text or '\n' in text: return False
 if b['height']>65 or b['width']>180: return False
 if cell_role=='khasra': return bool(re.fullmatch(r'[0-9/ min]+',text)) and (len(text)<=12)
 # Award-area candidates must look like one compact numeric value, not a
 # merged Khasra list or a paragraph that Table Transformer assigned badly.
 return bool(re.fullmatch(r'[0-9 .\-–—]+',text)) and '/' not in text and sum(c.isdigit() for c in text)>=2
def priority(text,r):
 s=1+len(text)/20
 if '/' in text or '-' in text: s+=2
 if 'min' in text: s+=3
 if not text.strip(): s+=4
 return s + (r%7)/100
def trim_to_single_ink_band(image):
 """Remove neighbouring-row ink accidentally included by a geometry crop."""
 gray=image.convert('L'); width,height=gray.size; rows=[]
 for y in range(height):
  count=sum(gray.getpixel((x,y))<190 for x in range(width))
  rows.append(count if count<width*.65 else 0) # ignore horizontal table rules
 bands=[]; start=None
 for y,count in enumerate(rows+[0]):
  if count and start is None: start=y
  if not count and start is not None: bands.append((start,y-1,sum(rows[start:y]))); start=None
 if not bands: return image
 top,bottom,_=max(bands,key=lambda x:x[2]); return image.crop((0,max(0,top-5),width,min(height,bottom+6)))
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--limit',type=int,default=100); ap.add_argument('--only-page',type=int); a=ap.parse_args(); a.output.mkdir(parents=True,exist_ok=True); gold=json.loads((a.root/'real-output/ocr-gold-manifest.json').read_text()); held={(2,x['row'],x['column']) for x in gold}; all=[]
 for folder in (a.root/'real-output/table-candidates').iterdir():
  p=page_number(folder.name); image_path=a.root/f'real-output/full-award-rapidocr/page-{p}.png' if p else None
  if not p or (a.only_page and p!=a.only_page) or not image_path.exists() or not (folder/'join.json').exists(): continue
  image=Image.open(image_path).convert('RGB')
  for table in json.loads((folder/'join.json').read_text()).get('tables',[]):
   for c in table['table']['cells']:
    if (p,c['row'],c['column']) in held: continue
    b=c['bounding_box']; cell_role=role(c['column'])
    if not readable_single_cell(c['text'],cell_role,b): continue
    horizontal_pad=8; vertical_pad=3; l=max(0,int(b['x'])-horizontal_pad); t=max(0,int(b['y'])-vertical_pad); rr=min(image.width,int(b['x']+b['width'])+horizontal_pad); bb=min(image.height,int(b['y']+b['height'])+vertical_pad); crop=a.output/f'p{p}-r{c["row"]}-c{c["column"]}.png'; trim_to_single_ink_band(image.crop((l,t,rr,bb))).save(crop); all.append({'id':crop.stem,'role':cell_role,'suggestion':c['text'],'sourcePage':p,'sourceRow':c['row'],'sourceColumn':c['column'],'sourceCrop':str(crop),'priority':priority(c['text'],c['row'])})
 # retain varied roles and pages rather than near-identical adjacent cells
 all.sort(key=lambda x:-x['priority']); chosen=[]; seen=set(); caps={'khasra':45,'recordedArea':28,'awardedArea':27}; counts={k:0 for k in caps}
 for x in all:
  bucket=(x['role'],x['sourcePage'],x['sourceRow']//3)
  if bucket in seen or counts[x['role']]>=caps[x['role']]: continue
  chosen.append(x); seen.add(bucket); counts[x['role']]+=1
  if len(chosen)>=a.limit: break
 (a.output/'label-queue.json').write_text(json.dumps(chosen,indent=2),encoding='utf-8'); print(json.dumps({'queued':len(chosen),'roles':{k:sum(x['role']==k for x in chosen) for k in set(x['role'] for x in chosen)}}))
if __name__=='__main__': main()
