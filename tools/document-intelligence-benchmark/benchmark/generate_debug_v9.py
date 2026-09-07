"""Create ignored, local visual debug pairs for incorrect recognition cells."""
import argparse, json, shutil
from pathlib import Path

def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--result',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); a.output.mkdir(parents=True,exist_ok=True)
 source=json.loads(a.result.read_text()); rows=[]
 for x in source['cells']:
  if x['semanticExact']: continue
  stem=x['id']; raw=a.output/f'{stem}-raw.png'; normalized=a.output/f'{stem}-normalized.png'
  shutil.copyfile(x['rawCrop'],raw); shutil.copyfile(x['normalizedCrop'],normalized)
  rows.append({'id':stem,'rawCrop':str(raw),'normalizedCrop':str(normalized),'gold':x['gold'],'prediction':x['prediction'],'role':x['role'],'confidence':x['confidence'],'errorType':x['errorType']})
 (a.output/'wrong-cells.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2),encoding='utf-8')
 print(json.dumps({'wrongCells':len(rows),'byType':{kind:sum(x['errorType']==kind for x in rows) for kind in sorted({x['errorType'] for x in rows})}},indent=2))
if __name__=='__main__': main()
