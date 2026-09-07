"""Hard fail if a training manifest contains held-out gold evidence coordinates."""
import json
from pathlib import Path
def gold_coordinates(path):
 return {(2,x['row'],x['column']) for x in json.loads(Path(path).read_text())}
def assert_no_gold_leak(items,gold_path):
 held=gold_coordinates(gold_path); leaked=[]
 for x in items:
  if {'sourcePage','sourceRow','sourceColumn'} <= x.keys() and (x['sourcePage'],x['sourceRow'],x['sourceColumn']) in held: leaked.append(x.get('id',x.get('image')))
 if leaked: raise ValueError(f'held-out gold leakage detected: {leaked[:5]}')
