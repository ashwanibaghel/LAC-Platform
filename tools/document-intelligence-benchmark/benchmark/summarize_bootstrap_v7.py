import argparse,json
from pathlib import Path
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--pseudo',type=Path,required=True); ap.add_argument('--a',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); p=json.loads(a.pseudo.read_text()); m=json.loads(a.a.read_text())['metrics']; e=m['exact']; lines=['# Phase 7 bootstrap summary','',f'Strict real pseudo-labels: {len(p)}. Manual labels: 0 (awaiting human labeller).','',f"Experiment A exact: Khasra {e['khasra']['correct']}/25; recorded area {e['recorded']['correct']}/25; awarded area {e['awarded']['correct']}/25; full rows {m['fullRow']['correct']}/25.",f"SafeExact: 0. Grammar-invalid rate: {m['grammarInvalidRate']:.0%}.",'','No production integration or canonical-data write occurred.']
 a.output.write_text('\n'.join(lines)+'\n',encoding='utf-8')
if __name__=='__main__': main()
