"""Recognition-only local shootout on unified held-out crops."""
import argparse,json,re,time
from pathlib import Path
from PIL import Image
from .specialist_grammar_v6 import normalize_area

def whitespace(text): return ' '.join(str(text or '').split())
def semantic(role,text): return whitespace(text) if role=='khasra' else normalize_area(text)
def classify(gold,pred,role):
 p=whitespace(pred); g=whitespace(gold)
 if not p: return 'recognizer hallucination'
 if role=='area' and '-' in g and '-' not in p: return 'dash lost'
 if role=='khasra' and '/' in g and '/' not in p: return 'slash lost'
 if re.sub(r'[^0-9]','',p)!=re.sub(r'[^0-9]','',g): return 'digit substitution'
 return 'other'
def metrics(cells):
 fields={};
 for f in ('khasra','recorded','awarded'):
  rows=[x for x in cells if x['field']==f]; fields[f]={'rawExact':sum(x['rawExact'] for x in rows),'semanticExact':sum(x['semanticExact'] for x in rows),'total':len(rows)}
 groups={}
 for x in cells: groups.setdefault(re.match(r'(r\d+)',x['id']).group(1),{})[x['field']]=x
 full=sum(all(g[f]['semanticExact'] for f in ('khasra','recorded','awarded')) for g in groups.values())
 sep=sum('-' in x['prediction'] for x in cells if x['role']=='area')
 return {'fields':fields,'fullRowSemanticExact':{'correct':full,'total':len(groups)},'separatorAccuracy':{'correct':sep,'total':sum(x['role']=='area' for x in cells)}}
def rapidocr(cells):
 from rapidocr import EngineType,RapidOCR
 engine=RapidOCR(params={'Det.engine_type':EngineType.TORCH,'Cls.engine_type':EngineType.TORCH,'Rec.engine_type':EngineType.TORCH})
 result=[]
 for x in cells:
  out=engine(Image.open(x['normalizedCrop']).convert('RGB')); result.append((whitespace(' '.join(str(v) for v in (out.txts or []))),None))
 return result
def doctr(cells):
 import numpy as np
 from doctr.models import recognition_predictor
 predictor=recognition_predictor('crnn_mobilenet_v3_large',pretrained=True)
 out=[]
 for x in cells:
  value=predictor([np.array(Image.open(x['normalizedCrop']).convert('RGB'))])[0]; out.append((value[0],float(value[1])))
 return out
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--manifests',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); ap.add_argument('--engine',choices=['rapidocr','doctr'],required=True); a=ap.parse_args(); data=json.loads(a.manifests.read_text()); held=data['gold']; started=time.perf_counter(); pred=rapidocr(held) if a.engine=='rapidocr' else doctr(held); cells=[]
 for x,(value,confidence) in zip(held,pred):
  row={k:x[k] for k in ('id','field','gold','role','rawCrop','normalizedCrop','preprocessing')}; row.update({'prediction':value,'confidence':confidence,'rawExact':whitespace(value)==whitespace(x['gold']),'semanticExact':semantic(x['role'],value)==semantic(x['role'],x['gold'])}); row['errorType']=None if row['semanticExact'] else classify(x['gold'],value,x['role']); cells.append(row)
 out={'engine':a.engine,'provenance':{'localOnly':True,'recognitionOnly':True,'pipelineVersion':'lac-cell-crop-v9.0','license':'Apache-2.0' if a.engine in ('rapidocr','doctr') else 'unknown'},'runtimeSeconds':round(time.perf_counter()-started,2),'metrics':metrics(cells),'cells':cells}; a.output.write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8'); print(json.dumps(out['metrics'],indent=2))
if __name__=='__main__': main()
