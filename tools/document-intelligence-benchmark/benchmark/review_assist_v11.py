"""Local keyboard-first OCR review; writes only ignored experimental training state."""
import argparse,json,time
from pathlib import Path
import tkinter as tk
from PIL import Image,ImageTk
from .cell_safety_v12 import normalize_area_evidence

def read(path,default): return json.loads(path.read_text()) if path.exists() else default
def identity(item): return f"{item['sourcePage']}:{item['sourceRow']}:{item['sourceColumn']}:{item['role']}"
def training_example(item,value,action,reviewer):
    return {'documentId':'local-award-review','page':item['sourcePage'],'boundingBox':item.get('sourceBox'),'cellRole':item['role'],'rawOcr':item.get('suggestion'),'multiViewPredictions':item.get('multiViewPredictions',[]),'finalHumanLabel':value,'wasCorrected':action=='corrected','verifiedAt':time.time(),'verifiedBy':reviewer,'sourceCrop':item['sourceCrop'],'sourceRow':item['sourceRow'],'sourceColumn':item['sourceColumn']}
def save_decision(state,item,value,action,reviewer):
    """One decision persists immediately; skips are intentionally not training gold."""
    key=identity(item)
    if key in {x['key'] for x in state['decisions']}: return state
    state['decisions'].append({'key':key,'action':action,'value':value,'at':time.time()})
    if action in ('accepted','corrected'): state['trainingExamples'].append(training_example(item,value,action,reviewer))
    return state
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--queue',type=Path,required=True); ap.add_argument('--state',type=Path,required=True); ap.add_argument('--reviewer',default=''); a=ap.parse_args(); queue=read(a.queue,[])[:30]; state=read(a.state,{'decisions':[],'trainingExamples':[],'startedAt':time.time()}); done={x['key'] for x in state['decisions']}; index=next((i for i,x in enumerate(queue) if identity(x) not in done),len(queue))
 root=tk.Tk(); root.title('LAC Award review assist — Enter accept | S skip | U uncertain | Q quit'); root.geometry('1180x760'); header=tk.Label(root,font=('Segoe UI',16,'bold')); header.pack(pady=8); crop_label=tk.Label(root); crop_label.pack(pady=6); detail=tk.Label(root,font=('Segoe UI',11),justify='left'); detail.pack(); entry=tk.Entry(root,font=('Consolas',22),width=36); entry.pack(pady=12); note=tk.Label(root,text='Enter: accept/edit · 1: OCR suggestion · S: skip · U: uncertain · Q: save and quit. Every action saves immediately.',font=('Segoe UI',11)); note.pack()
 def persist(): a.state.parent.mkdir(parents=True,exist_ok=True); a.state.write_text(json.dumps(state,ensure_ascii=False,indent=2),encoding='utf-8')
 def show():
  nonlocal index
  while index<len(queue) and identity(queue[index]) in {x['key'] for x in state['decisions']}: index+=1
  if index>=len(queue): persist(); root.destroy(); return
  x=queue[index]; im=Image.open(x['sourceCrop']).convert('RGB'); im.thumbnail((850,260)); im=im.resize((im.width*3,im.height*3)); photo=ImageTk.PhotoImage(im); crop_label.configure(image=photo); crop_label.image=photo
  header.configure(text=f'Review field {index+1} of {len(queue)} · {x["role"]}')
  area=normalize_area_evidence(x.get('suggestion','')) if x['role'] in ('area','recordedArea','awardedArea') else None; normalized=f'\nNormalized: {area["normalizedValue"]} ({area["normalizationReason"] or "no formatting change"})' if area else ''
  detail.configure(text=f'Source cell: page {x["sourcePage"]}, table row {x["sourceRow"]}, column {x["sourceColumn"]}\nRaw OCR: {x.get("suggestion","")}{normalized}\nMaster suggestions — verify from source: {", ".join(x.get("masterSuggestions",[])) or "not available"}\nThe enlarged crop is convenience; retain the original PDF as evidence.')
  entry.delete(0,tk.END); entry.insert(0,x.get('suggestion','')); entry.focus_set()
 def decide(action):
  nonlocal index
  x=queue[index]; value=entry.get().strip(); actual='accepted' if action=='accept' and value==x.get('suggestion','') else ('corrected' if action=='accept' else action)
  save_decision(state,x,value,actual,a.reviewer); persist(); index+=1; show()
 root.bind('<Return>',lambda e:decide('accept')); root.bind('1',lambda e:(entry.delete(0,tk.END),entry.insert(0,queue[index].get('suggestion','')))); root.bind('s',lambda e:decide('skipped')); root.bind('S',lambda e:decide('skipped')); root.bind('u',lambda e:decide('uncertain')); root.bind('U',lambda e:decide('uncertain')); root.bind('q',lambda e:(persist(),root.destroy())); root.bind('Q',lambda e:(persist(),root.destroy())); show(); root.mainloop()
if __name__=='__main__': main()
