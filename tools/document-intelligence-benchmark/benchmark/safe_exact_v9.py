"""Experimental SafeExact gate for a shootout result; never changes production."""
import argparse,json
from pathlib import Path
from .specialist_grammar_v6 import valid_area,valid_khasra

def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--result',type=Path,required=True); ap.add_argument('--master',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); result=json.loads(a.result.read_text()); master={x['displayNumber'] for x in json.loads(a.master.read_text())}; rows={}
 for cell in result['cells']: rows.setdefault(cell['id'].split('-')[0],{})[cell['field']]=cell
 accepted=[]
 for ident,row in rows.items():
  k=row['khasra']; areas=[row['recorded'],row['awarded']]
  candidate=valid_khasra(k['prediction']) and k['prediction'] in master and all(valid_area(x['prediction']) for x in areas)
  if candidate: accepted.append({'row':ident,'prediction':{f:row[f]['prediction'] for f in row},'correct':all(row[f]['semanticExact'] for f in row)})
 correct=sum(x['correct'] for x in accepted); out={'experimentalOnly':True,'productionSafeExactChanged':False,'candidates':len(accepted),'correct':correct,'falsePositives':len(accepted)-correct,'precision':correct/len(accepted) if accepted else None,'rows':accepted}; a.output.write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps(out,indent=2))
if __name__=='__main__': main()
