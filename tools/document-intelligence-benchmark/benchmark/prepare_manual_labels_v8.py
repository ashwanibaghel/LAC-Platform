"""Prepare local human labels for training while retaining original visible transcription."""
import argparse,json
from pathlib import Path
from .specialist_grammar_v6 import normalize_area,valid_khasra,valid_area
from .training_guard_v8 import assert_no_gold_leak
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--labels',type=Path,required=True); ap.add_argument('--gold',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); labels=json.loads(a.labels.read_text()); assert_no_gold_leak(labels,a.gold); out=[]
 for x in labels:
  role='khasra' if x['role']=='khasra' else 'area'; normalized=x['text'] if role=='khasra' else normalize_area(x['text'])
  if not (valid_khasra(normalized) if role=='khasra' else valid_area(normalized)): continue
  out.append({'image':x['sourceCrop'],'text':normalized,'visibleText':x['text'],'role':role,'sourcePage':x['sourcePage'],'sourceRow':x['sourceRow'],'sourceColumn':x['sourceColumn'],'labelSource':'human'})
 a.output.write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps({'usable':len(out),'excluded':len(labels)-len(out),'areasNormalized':sum(x['role']=='area' and x['text']!=normalize_area(x['text']) for x in labels)}))
if __name__=='__main__': main()
