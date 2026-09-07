"""Create local cell crops from a reviewed manifest and Table Transformer geometry."""
import argparse,json
from pathlib import Path
from PIL import Image
def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--root',type=Path,required=True); ap.add_argument('--manifest',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); a.output.mkdir(parents=True,exist_ok=True)
    manifest=json.loads(a.manifest.read_text()); out=[]
    geom=json.loads((a.root/'real-output/table-candidates/page-2/geometry.json').read_text())['geometry']; rows=sorted([x['box'] for x in geom if x['label']=='table row'],key=lambda b:b['y']); cols=sorted([x['box'] for x in geom if x['label']=='table column'],key=lambda b:b['x']); image=Image.open(a.root/'real-output/full-award-rapidocr/page-2.png').convert('RGB')
    for item in manifest:
        row=rows[item['row']]; col=cols[item['column']]
        # Table Transformer columns span the full table height.  Use the
        # column's x extent and the row's y extent; intersecting both boxes
        # truncated cells and made OCR detector recall artificially poor.
        pad=12
        l=max(0,int(col['x'])-pad); t=max(0,int(row['y'])-pad)
        r=min(image.width,int(col['x']+col['width'])+pad)
        b=min(image.height,int(row['y']+row['height'])+pad)
        path=a.output/f"{item['id']}.png"; image.crop((l,t,r,b)).save(path)
        out.append({**item,'crop':str(path),'box':{'x':l,'y':t,'width':r-l,'height':b-t}})
    (a.output/'manifest-with-crops.json').write_text(json.dumps(out,indent=2),encoding='utf-8'); print(json.dumps({'crops':len(out),'output':str(a.output)},indent=2))
if __name__=='__main__': main()
