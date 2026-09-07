"""Heading-driven claims-region crop for a targeted Table Transformer pass."""
import argparse,json
from pathlib import Path
from PIL import Image
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--pages',nargs='+',type=int,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); a.output.mkdir(parents=True,exist_ok=True)
    for p in a.pages:
        raw=json.loads((a.root/'real-output'/'full-award-rapidocr'/f'page-{p}.raw.json').read_text()); hits=[w for w in raw if 'claim' in str(w.get('txt','')).lower()]
        if not hits: continue
        y=min(float(w['box'][0][1]) for w in hits); image=Image.open(a.root/'real-output'/'full-award-rapidocr'/f'page-{p}.png').convert('RGB'); top=max(0,int(y)-40); crop=image.crop((0,top,image.width,image.height)); crop.save(a.output/f'page-{p}-claims.png'); print(json.dumps({'page':p,'headingY':y,'cropTop':top,'path':str(a.output/f'page-{p}-claims.png')}))
if __name__=='__main__': main()
