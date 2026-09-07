"""Small LAC cell grammars. Validation rejects; it never repairs with master data."""
import re
AREA_RE=re.compile(r'^\d{1,3}-\d{1,2}$')
KHASRA_RE=re.compile(r'^\d+//\d+(?:/\d+)*(?: ?min)?$')
KILLA_RE=re.compile(r'^\d+(?:/\d+)?$')
QUALIFIERS={'min'}
def normalize_area(s):
    return re.sub(r'\s*[-–—]+\s*','-',str(s).strip())
def valid_area(s):
    s=normalize_area(s)
    if not AREA_RE.fullmatch(s): return False
    b=int(s.split('-')[1]); return 0<=b<=19
def valid_khasra(s):
    if not KHASRA_RE.fullmatch(s): return False
    tail=s.split('//',1)[1].removesuffix(' min').removesuffix('min')
    return all(valid_killa(x) for x in [tail.split('/')[0]]) and all(part.isdigit() for part in tail.split('/')[1:])
def valid_killa(s):
    if not KILLA_RE.fullmatch(s): return False
    return all(0<=int(x)<=99 for x in s.split('/'))
def valid_role(role,s):
    return {'rectangle':s.isdigit(),'killa':valid_killa(s),'khasra':valid_khasra(s),'area':valid_area(s),'qualifier':s in QUALIFIERS}.get(role,False)
