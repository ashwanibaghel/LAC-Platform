"""Small LAC cell grammars. Validation rejects; it never repairs with master data."""
import re
AREA_RE=re.compile(r'^\d{1,3}-(?:0[0-1]|[1-9]\d|\d)$')
KHASRA_RE=re.compile(r'^\d+//\d+(?:/\d+)?(?: min)?$')
KILLA_RE=re.compile(r'^\d+(?:/\d+)?$')
QUALIFIERS={'min'}
def valid_area(s):
    if not AREA_RE.fullmatch(s): return False
    b=int(s.split('-')[1]); return 0<=b<=19
def valid_khasra(s):
    if not KHASRA_RE.fullmatch(s): return False
    return valid_killa(s.split('//',1)[1].removesuffix(' min'))
def valid_killa(s):
    if not KILLA_RE.fullmatch(s): return False
    return all(0<=int(x)<=99 for x in s.split('/'))
def valid_role(role,s):
    return {'rectangle':s.isdigit(),'killa':valid_killa(s),'khasra':valid_khasra(s),'area':valid_area(s),'qualifier':s in QUALIFIERS}.get(role,False)
