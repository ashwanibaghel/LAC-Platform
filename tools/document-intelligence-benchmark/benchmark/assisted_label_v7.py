"""Local Tk assisted labeller. Enter saves exact visible text; Escape skips; no database writes."""
import argparse,json,time
from pathlib import Path
import tkinter as tk
from PIL import Image,ImageTk
def main():
 ap=argparse.ArgumentParser(); ap.add_argument('--queue',type=Path,required=True); ap.add_argument('--output',type=Path,required=True); a=ap.parse_args(); queue=json.loads(a.queue.read_text()); labels=json.loads(a.output.read_text()) if a.output.exists() else []; state={'i':0}
 root=tk.Tk(); root.title('LAC local cell labeller'); image=tk.Label(root); image.pack(padx=20,pady=10); info=tk.Label(root,font=('Segoe UI',12)); info.pack(); entry=tk.Entry(root,font=('Consolas',18),width=32); entry.pack(padx=20,pady=12)
 def show():
  if state['i']>=len(queue): root.destroy(); return
  x=queue[state['i']]; im=Image.open(x['sourceCrop']).convert('RGB'); im.thumbnail((900,300)); photo=ImageTk.PhotoImage(im.resize((im.width*3,im.height*3))); image.configure(image=photo); image.image=photo; info.configure(text=f"{state['i']+1}/{len(queue)}  Role: {x['role']}  RapidOCR: {x['suggestion']}"); entry.delete(0,tk.END); entry.insert(0,x['suggestion']); entry.focus_set()
 def save(_=None):
  x=queue[state['i']]; text=entry.get().strip();
  if text: labels.append({**x,'text':text,'labelledAt':time.time()}); a.output.write_text(json.dumps(labels,indent=2),encoding='utf-8')
  state['i']+=1; show()
 def skip(_=None): state['i']+=1; show()
 root.bind('<Return>',save); root.bind('<Escape>',skip); show(); root.mainloop()
if __name__=='__main__': main()
