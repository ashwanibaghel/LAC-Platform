"""Run docTR on a small local PDF page range; benchmark-only, no production writes."""
import argparse,json,time
from pathlib import Path
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--pdf',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args()
    from doctr.io import DocumentFile
    from doctr.models import ocr_predictor
    started=time.perf_counter(); doc=DocumentFile.from_pdf(str(a.pdf))[:3]
    model=ocr_predictor(pretrained=True); result=model(doc)
    exported=result.export(); a.output.write_text(json.dumps({'pages':len(doc),'runtimeSeconds':round(time.perf_counter()-started,2),'result':exported},ensure_ascii=False),encoding='utf-8')
    print(json.dumps({'pages':len(doc),'runtimeSeconds':round(time.perf_counter()-started,2),'output':str(a.output)}))
if __name__=='__main__': main()
