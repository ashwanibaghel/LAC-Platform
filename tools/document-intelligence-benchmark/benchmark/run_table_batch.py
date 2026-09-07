"""Run Table Transformer detector/structure models once across many page images."""
from __future__ import annotations
import argparse, json, time
from pathlib import Path
from run_table_pipeline import complete_resize, infer

def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--images', nargs='+', type=Path, required=True)
    ap.add_argument('--output-root', type=Path, required=True)
    ap.add_argument('--threshold', type=float, default=.7)
    args = ap.parse_args()
    from PIL import Image
    import torch
    from transformers import AutoImageProcessor, TableTransformerForObjectDetection
    dp = AutoImageProcessor.from_pretrained('microsoft/table-transformer-detection')
    sp = AutoImageProcessor.from_pretrained('microsoft/table-transformer-structure-recognition-v1.1-all')
    complete_resize(dp); complete_resize(sp)
    detector = TableTransformerForObjectDetection.from_pretrained('microsoft/table-transformer-detection', dilation=False)
    structurer = TableTransformerForObjectDetection.from_pretrained('microsoft/table-transformer-structure-recognition-v1.1-all', dilation=False)
    for image_path in args.images:
        started=time.perf_counter(); image=Image.open(image_path).convert('RGB')
        geometry=[]; tables=0
        for table in infer(dp, detector, image, torch, args.threshold):
            if table['label']!='table': continue
            x,y=max(0,int(table['box']['x'])),max(0,int(table['box']['y']))
            right=min(image.width,int(table['box']['x']+table['box']['width'])); bottom=min(image.height,int(table['box']['y']+table['box']['height']))
            if right<=x or bottom<=y: continue
            tables+=1; geometry.append({'label':'table','score':table['score'],'box':{'x':x,'y':y,'width':right-x,'height':bottom-y}})
            for item in infer(sp, structurer, image.crop((x,y,right,bottom)), torch, .5):
                if item['label']=='table': continue
                item['box']['x']+=x; item['box']['y']+=y; geometry.append(item)
        labels={label:sum(1 for i in geometry if i['label']==label) for label in sorted({i['label'] for i in geometry})}
        od=args.output_root/image_path.stem; od.mkdir(parents=True,exist_ok=True)
        (od/'geometry.json').write_text(json.dumps({'summary':{'engine':'Table Transformer batch','seconds':round(time.perf_counter()-started,3),'tables_found':tables,'labels':labels},'geometry':geometry},indent=2),encoding='utf-8')
        print(json.dumps({'page':image_path.stem,'tables_found':tables,'labels':labels},sort_keys=True),flush=True)
if __name__=='__main__': main()
