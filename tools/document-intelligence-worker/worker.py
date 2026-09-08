"""Local-only contract v1 bridge around the installed RapidOCR/Table benchmark stack."""
import argparse,json,sys,time
from pathlib import Path
CONTRACT_VERSION=1
def fail(message): print(message,file=sys.stderr); return 2
def main():
 p=argparse.ArgumentParser();p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 try: data=json.loads(a.input.read_text())
 except Exception: return fail('invalid worker input JSON')
 if data.get('contractVersion')!=CONTRACT_VERSION:return fail('unsupported contract version')
 pdf=Path(data.get('filePath',''))
 if not pdf.is_file() or pdf.suffix.lower()!='.pdf':return fail('local PDF not found')
 try:
  import fitz
  from PIL import Image
  from rapidocr import EngineType,RapidOCR
  engine=RapidOCR(params={'Det.engine_type':EngineType.TORCH,'Cls.engine_type':EngineType.TORCH,'Rec.engine_type':EngineType.TORCH}); doc=fitz.open(pdf); candidates=[]; started=time.time()
  for n,page in enumerate(doc,1):
   pix=page.get_pixmap(matrix=fitz.Matrix(2,2),alpha=False); image=Image.frombytes('RGB',[pix.width,pix.height],pix.samples); out=engine(image)
   for text,box,score in zip(out.txts or [],out.boxes or [],out.scores or []):
    raw=' '.join(str(text).split()); xs=[float(x[0]) for x in box];ys=[float(x[1]) for x in box]; region={'x':min(xs),'y':min(ys),'width':max(xs)-min(xs),'height':max(ys)-min(ys)}
    if '//' in raw: candidates.append({'candidateType':'AwardKhasra','structuredPayload':{'khasraNumber':raw,'qualifier':None},'page':n,'sourceRegion':region,'rawSourceText':raw,'rawOcr':raw,'normalizedSuggestion':raw,'normalizationReason':None,'confidence':float(score),'interpretationWarnings':['OCR suggestion; human review required']})
   # Preserve non-table/narrative OCR as reviewable evidence, never discard it.
   if out.txts: candidates.append({'candidateType':'UnmappedAwardFinding','structuredPayload':{'category':'Local OCR narrative','summary':'Locally detected narrative evidence','extractedText':None},'page':n,'sourceRegion':None,'rawSourceText':'','rawOcr':None,'normalizedSuggestion':None,'normalizationReason':None,'confidence':None,'interpretationWarnings':['Narrative retained for human review']})
  result={'contractVersion':1,'documentId':data['documentId'],'status':'Completed','pagesProcessed':len(doc),'candidates':candidates,'warnings':['Table geometry adapter is optional; candidate review remains human-required.'],'metrics':{'runtimeSeconds':round(time.time()-started,2),'engine':'RapidOCR local'}};a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(result));return 0
 except Exception as e: return fail(f'local worker failed: {type(e).__name__}')
if __name__=='__main__':sys.exit(main())
