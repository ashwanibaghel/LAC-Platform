"""Rebuild local labelled and held-out crops with the one v9 crop pipeline."""
import argparse, json
from pathlib import Path
from PIL import Image
from .cell_crop_pipeline_v9 import cell_identity, normalize_from_page
from .training_guard_v8 import assert_no_gold_leak

def cells_for(root, page):
    data=json.loads((root/f'real-output/table-candidates/page-{page}/join.json').read_text())
    return {(c['row'],c['column']):c['bounding_box'] for t in data.get('tables',[]) for c in t['table']['cells']}

def field_role(field): return 'khasra' if field=='khasra' else 'area'

def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--human',type=Path); ap.add_argument('--pseudo',type=Path); ap.add_argument('--gold',type=Path,required=True); a=ap.parse_args()
 a.output.mkdir(parents=True,exist_ok=True); rawdir=a.output/'raw'; normdir=a.output/'normalized'; rawdir.mkdir(exist_ok=True); normdir.mkdir(exist_ok=True)
 cache={}; pages={}
 gold_geometry=None
 def gold_box(row,col):
  nonlocal gold_geometry
  if gold_geometry is None:
   geometry_data=json.loads((a.root/'real-output/table-candidates/page-2/geometry.json').read_text())['geometry']
   gold_geometry=(sorted([x['box'] for x in geometry_data if x['label']=='table row'],key=lambda b:b['y']), sorted([x['box'] for x in geometry_data if x['label']=='table column'],key=lambda b:b['x']))
  rows,columns=gold_geometry; r=rows[row]; c=columns[col]
  # A Table Transformer row and column define the source cell.  This is the
  # same evidence geometry used by the original gold manifest.
  return {'x':c['x'],'y':r['y'],'width':c['width'],'height':r['height']}
 def geometry(page,row,col):
  if page not in cache: cache[page]=cells_for(a.root,page); pages[page]=Image.open(a.root/f'real-output/full-award-rapidocr/page-{page}.png').convert('RGB')
  return cache[page][(row,col)]
 def emit(item, source):
  page,row,col=item['sourcePage'],item['sourceRow'],item['sourceColumn']; box=item.get('sourceBox') or geometry(page,row,col)
  if page not in pages: pages[page]=Image.open(a.root/f'real-output/full-award-rapidocr/page-{page}.png').convert('RGB')
  raw,norm,meta=normalize_from_page(pages[page],box)
  ident=cell_identity(page,row,col); stem=f'{source}-{page}-{row}-{col}'
  rawpath=rawdir/f'{stem}.png'; normpath=normdir/f'{stem}.png'; raw.save(rawpath); norm.save(normpath)
  return {**item,'id':item.get('id',stem),'identity':ident,'labelSource':item.get('labelSource',source),'role':item.get('role') or field_role(item.get('field','')),'rawCrop':str(rawpath),'image':str(normpath),'normalizedCrop':str(normpath),'preprocessing':meta}
 gold=[]
 for x in json.loads(a.gold.read_text()):
  y={**x,'sourcePage':2,'sourceRow':x['row'],'sourceColumn':x['column'],'sourceBox':gold_box(x['row'],x['column']),'role':field_role(x['field']),'labelSource':'heldoutGold'}; gold.append(emit(y,'gold'))
 result={'gold':gold}
 for name,path in [('human',a.human),('pseudo',a.pseudo)]:
  if path:
   records=[emit(x,name) for x in json.loads(path.read_text())]
   assert_no_gold_leak(records,a.gold)
   result[name]=records
 (a.output/'unified-manifests.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
 print(json.dumps({k:len(v) for k,v in result.items()}))
if __name__=='__main__': main()
