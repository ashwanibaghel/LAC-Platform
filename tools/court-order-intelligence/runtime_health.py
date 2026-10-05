"""Read-only loopback probes; never start, download or invoke inference."""
import hashlib
import json
from pathlib import Path
import requests

def root_fingerprint(root):
    return hashlib.sha256(str(Path(root).resolve()).lower().encode()).hexdigest()

def model_state():
    try:
        session=requests.Session();session.trust_env=False
        with session,session.get('http://127.0.0.1:8096/health',timeout=(0.25,0.75),allow_redirects=False,stream=True) as r:
            data=r.raw.read(4097)
            if len(data)>4096: return 'Failed'
            if r.status_code==503: return 'Starting'
            return 'Ready' if r.status_code==200 and json.loads(data).get('status')=='ok' else 'Failed'
    except (requests.RequestException,ValueError): return 'ModelOffline'

def case_state(artifact):
    from semantics import usable_facts
    orders=(artifact or {}).get('orders',[])
    if any(usable_facts(o) for o in orders): return 'Ready'
    if any(o.get('status')=='NeedsSourceReview' for o in orders): return 'SourceBlocked'
    return 'CaseNotReady'
