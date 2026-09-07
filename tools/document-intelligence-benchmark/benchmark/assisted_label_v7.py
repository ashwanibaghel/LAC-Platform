"""Fast local Tk labeller: Enter accepts, S skips, Q saves/quits; never writes a database."""
import argparse,json,time
from pathlib import Path
import tkinter as tk
from PIL import Image,ImageTk
def read(path,default): return json.loads(path.read_text()) if path.exists() else default
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--queue',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); queue=read(a.queue,[]); labels=read(a.output,[]); progress_path=a.output.with_suffix('.progress.json'); progress=read(progress_path,{'startedAt':time.time(),'skipped':[]}); done={x['id'] for x in labels}|set(progress['skipped']); state={'i':next((i for i,x in enumerate(queue) if x['id'] not in done),len(queue))}
 root=tk.Tk(); root.title('LAC cell labeller — Enter save | S skip | Q quit'); root.geometry('1000x650'); image=tk.Label(root); image.pack(padx=20,pady=12); info=tk.Label(root,font=('Segoe UI',13),justify='left'); info.pack(); hint=tk.Label(root,text='Type only the one visible value in the crop. If the crop contains multiple values or is unclear, press S.',font=('Segoe UI',11)); hint.pack(pady=5); entry=tk.Entry(root,font=('Consolas',20),width=36); entry.pack(padx=20,pady=14)
 def persist():
  a.output.write_text(json.dumps(labels,indent=2),encoding='utf-8'); progress.update({'nextIndex':state['i'],'updatedAt':time.time(),'labelled':len(labels),'skippedCount':len(progress['skipped'])}); progress_path.write_text(json.dumps(progress,indent=2),encoding='utf-8')
 def show():
  while state['i']<len(queue) and queue[state['i']]['id'] in ({x['id'] for x in labels}|set(progress['skipped'])): state['i']+=1
  if state['i']>=len(queue): persist(); root.destroy(); return
  x=queue[state['i']]; im=Image.open(x['sourceCrop']).convert('RGB'); im.thumbnail((700,220)); photo=ImageTk.PhotoImage(im.resize((im.width*3,im.height*3))); image.configure(image=photo); image.image=photo; info.configure(text=f"Cell {state['i']+1} of {len(queue)}     Expected: {x['role']}\nOCR suggestion: {x['suggestion']}\nEvidence: page {x['sourcePage']} · table row {x['sourceRow']} · column {x['sourceColumn']}"); entry.delete(0,tk.END); entry.insert(0,x['suggestion']); entry.focus_set()
 def save(_=None):
  x=queue[state['i']]; text=entry.get().strip()
  if text: labels.append({**x,'text':text,'action':'accepted' if text==x['suggestion'] else 'corrected','labelledAt':time.time()})
  state['i']+=1; persist(); show()
 def skip(_=None): progress['skipped'].append(queue[state['i']]['id']); state['i']+=1; persist(); show()
 def quit(_=None): persist(); root.destroy()
 root.bind('<Return>',save); root.bind('s',skip); root.bind('S',skip); root.bind('<Escape>',skip); root.bind('q',quit); root.bind('Q',quit); show(); root.mainloop()
if __name__=='__main__': main()
