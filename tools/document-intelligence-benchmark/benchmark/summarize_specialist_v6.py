import argparse,json
from pathlib import Path
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--input',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); x=json.loads(a.input.read_text()); e=x['metrics']['exact']; s=x['safeExact']; lines=['# Phase 6 LAC-specific cell OCR experiment','', 'Architecture: small custom CRNN + CTC sequence recognizer in PyTorch; synthetic-only training; CPU inference. PyTorch is BSD-3-Clause; no pretrained/cloud model.','',f"Synthetic training samples: {x['training']['samples']} (fictional); training runtime: {x['training']['runtimeSeconds']} s; model checkpoint is ignored.",'', '## Held-out real gold metrics','', '| Field | Exact |','|---|---:|']
 for k,v in e.items(): lines.append(f"| {k} | {v['correct']}/{v['total']} ({v['accuracy']:.1%}) |")
 lines += [f"| Full row | {x['metrics']['fullRow']['correct']}/{x['metrics']['fullRow']['total']} ({x['metrics']['fullRow']['accuracy']:.1%}) |",'',f"Separator accuracy: {x['metrics']['separatorAccuracy']:.1%}",f"SafeExact: {s['count']} candidates; precision {s['precision']}",'','Conclusion: synthetic-only training did not overcome the scanned-award domain gap. Keep specialist OCR out of production and SafeExact disabled. A separate 50–100-cell real training set may be justified only after improving crop normalization and adding validation data.']
 a.output.write_text('\n'.join(lines)+'\n',encoding='utf-8')
if __name__=='__main__': main()
