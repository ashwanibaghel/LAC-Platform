"""Build strict, provenance-preserving pseudo labels; master confirms but never repairs OCR."""
import argparse,json,re
from pathlib import Path
from PIL import Image
from .specialist_grammar_v6 import valid_khasra
def page_number(name):
 m=re.fullmatch(r'page-(\d+)',name); return int(m.group(1)) if m else None
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--master',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); a.output.mkdir(parents=True,exist_ok=True)
 master={x['displayNumber'] for x in json.loads(a.master.read_text())}; gold=json.loads((a.root/'real-output/ocr-gold-manifest.json').read_text()); held={(2,x['row'],x['column']) for x in gold}
 accepted=[]; rejected=[]
 for folder in (a.root/'real-output/table-candidates').iterdir():
  p=page_number(folder.name)
  if p is None or not (folder/'join.json').exists(): continue
  image_path=a.root/f'real-output/full-award-rapidocr/page-{p}.png'
  if not image_path.exists(): continue
  image=Image.open(image_path).convert('RGB')
  for table in json.loads((folder/'join.json').read_text()).get('tables',[]):
   for cell in table['table']['cells']:
    if cell['column'] not in (0,3,6): continue
    raw=cell['text'].strip(); key=(p,cell['row'],cell['column'])
    if key in held: rejected.append({'reason':'held-out-gold','source':key}); continue
    # Exact string, strict grammar and direct table-cell role only. No lookup transforms raw OCR.
    if not valid_khasra(raw): rejected.append({'reason':'grammar','raw':raw,'source':key}); continue
    if raw not in master: rejected.append({'reason':'not-exact-master','raw':raw,'source':key}); continue
    b=cell['bounding_box']; pad=8; l=max(0,int(b['x'])-pad); t=max(0,int(b['y'])-pad); r=min(image.width,int(b['x']+b['width'])+pad); bot=min(image.height,int(b['y']+b['height'])+pad); crop=a.output/f'p{p}-r{cell["row"]}-c{cell["column"]}.png'; image.crop((l,t,r,bot)).save(crop)
    accepted.append({'text':raw,'role':'khasra','sourcePage':p,'sourceRow':cell['row'],'sourceColumn':cell['column'],'sourceCrop':str(crop),'sourceBox':b,'rawOcr':raw,'masterConfirmedExact':True,'masterRepairUsed':False,'qualifierExact':True,'inheritedRectangle':False,'alternativesObserved':[]})
 (a.output/'pseudo-labels.json').write_text(json.dumps(accepted,indent=2),encoding='utf-8'); (a.output/'rejected.json').write_text(json.dumps(rejected,indent=2),encoding='utf-8'); print(json.dumps({'accepted':len(accepted),'rejected':len(rejected),'heldOutExcluded':sum(x.get('reason')=='held-out-gold' for x in rejected)},indent=2))
if __name__=='__main__': main()
