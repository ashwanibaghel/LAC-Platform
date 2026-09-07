"""Small supplementary synthetic subset compatible with docTR's no-space vocabulary."""
import argparse,json
from pathlib import Path
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--input',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--limit',type=int,default=200); a=ap.parse_args(); out=[]
 for x in json.loads(a.input.read_text()):
  text=x['text'].replace(' min','min')
  if ' ' not in text: out.append({'image':x['image'],'text':text,'role':x['role'],'labelSource':'synthetic'})
  if len(out)>=a.limit: break
 a.output.write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps({'samples':len(out)}))
if __name__=='__main__': main()
