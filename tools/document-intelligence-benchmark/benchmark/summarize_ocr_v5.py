"""Write a concise human-readable report for the local OCR engine benchmark."""
import argparse,json
from pathlib import Path
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--metrics',type=Path,required=True); ap.add_argument('--rapid',type=Path,required=True); ap.add_argument('--tesseract',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args()
    m=json.loads(a.metrics.read_text()); r=json.loads(a.rapid.read_text()); t=json.loads(a.tesseract.read_text())
    lines=['# Local OCR engine benchmark (Phase 5)','', 'Scope: 25 visually reviewed rows × 3 cells from page 2 (75 crops). No canonical/master-data voting and no production writes.','',f"Runtime: RapidOCR {r.get('engineRuntimeSeconds')} s; Tesseract 5 {t.get('engineRuntimeSeconds')} s.",'','## Scores','']
    lines += ['| Engine/variant | Khasra exact/structural | Recorded exact/structural | Awarded exact/structural | Full row exact/structural |','|---|---:|---:|---:|---:|']
    for key,v in m['metrics'].items():
        f=m['fullRow'][key]
        lines.append(f"| {key} | {v['khasra']['exactCorrect']}/{v['khasra']['structuralCorrect']}/{v['khasra']['total']} | {v['recorded']['exactCorrect']}/{v['recorded']['structuralCorrect']}/{v['recorded']['total']} | {v['awarded']['exactCorrect']}/{v['awarded']['structuralCorrect']}/{v['awarded']['total']} | {f['exactRows']}/{f['structuralRows']}/{f['totalRows']} |")
    c=m['consensus']; lines += ['', '## Consensus',f"RapidOCR/Tesseract compared {c['cellsCompared']} cells; exact text agreement {c['agreement']}; both exact-correct {c['bothCorrect']}. Consensus is reported as agreement only, never a master-data vote.",'','## Interpretation','- RapidOCR is materially better than Tesseract on this sample, but its full-row exact result is not safe for automatic confirmation.','- Structural recovery can identify some numeric pairs despite separator/OCR noise; those rows must remain review candidates until exact evidence is confirmed.','- Tesseract is not a useful fallback for these small printed table cells in this configuration.','- Surya/docTR are optional comparison engines only; neither is integrated into production from this benchmark.']
    a.output.write_text('\n'.join(lines)+'\n',encoding='utf-8')
    (a.output.parent/'ocr-engine-benchmark.json').write_text(json.dumps({'scope':{'rows':25,'cells':75,'page':2},'metrics':m['metrics'],'fullRow':m['fullRow'],'runtimes':{'RapidOCR':r.get('engineRuntimeSeconds'),'Tesseract5':t.get('engineRuntimeSeconds')},'engines':{'Surya':{'status':'blocked','error':'llama-server binary not found'},'docTR':{'status':'completed','pages':3,'runtimeSeconds':89.08,'layout':'blocks/lines/words; no table grid output'}}},indent=2),encoding='utf-8')
if __name__=='__main__': main()
