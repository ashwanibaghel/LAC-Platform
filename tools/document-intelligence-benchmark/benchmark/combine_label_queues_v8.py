"""Combine curated local queues without duplicating source evidence coordinates."""
import argparse,json
from pathlib import Path
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--inputs',type=Path,nargs='+',required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--limit',type=int,default=50); a=ap.parse_args(); out=[]; seen=set()
 for p in a.inputs:
  for x in json.loads(p.read_text()):
   key=(x['sourcePage'],x['sourceRow'],x['sourceColumn'])
   if key not in seen and len(out)<a.limit: out.append(x); seen.add(key)
 a.output.parent.mkdir(parents=True,exist_ok=True); a.output.write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps({'queued':len(out)}))
if __name__=='__main__': main()
