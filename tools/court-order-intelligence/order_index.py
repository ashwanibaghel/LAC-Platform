"""Known-order metadata and explicit date-only lazy intelligence, never discovery."""
import json
import re
import uuid
import time
from contextlib import contextmanager
from datetime import date
from pathlib import Path
from urllib.parse import urlsplit
from semantics import identity, VERSION

MONTHS={name:index for index,names in enumerate((
    'jan january','feb february','mar march','apr april','may','jun june',
    'jul july','aug august','sep sept september','oct october','nov november','dec december'),1)
    for name in names.split()}

def requested_date(question, known_dates):
    text=question.lower()
    numeric=re.search(r'(?<!\d)(\d{4})-(\d{2})-(\d{2})(?!\d)',text)
    if numeric: parts=(int(numeric[1]),int(numeric[2]),int(numeric[3]))
    else:
        numeric=re.search(r'(?<!\d)(\d{1,2})[.\-/](\d{1,2})[.\-/](\d{4})(?!\d)',text)
        if numeric: parts=(int(numeric[3]),int(numeric[2]),int(numeric[1]))
        else:
            named=re.search(r'\b(\d{1,2})(?:st|nd|rd|th)?\s+('+ '|'.join(MONTHS)+r')\b(?:\s+(\d{4}))?',text)
            if not named: return {'requested':False,'date':None}
            month,day=MONTHS[named[2]],int(named[1])
            if named[3]: parts=(int(named[3]),month,day)
            else:
                matches={value for value in known_dates if value and value[5:]==f'{month:02d}-{day:02d}'}
                return {'requested':True,'date':next(iter(matches)) if len(matches)==1 else None,
                        'ambiguous':len(matches)>1}
    try: value=date(*parts).isoformat()
    except ValueError: value=None
    return {'requested':True,'date':value}

def official_pdf(url):
    try:
        uri=urlsplit(url)
        return (uri.scheme=='https' and uri.hostname=='delhihighcourt.nic.in'
                and uri.port in (None,443) and not uri.username and not uri.password
                and not uri.query and not uri.fragment and uri.path.startswith((
                    '/app/showlogo/','/app/showFileJudgment/','/app/case_number_pdf/','/app/downloadOrderbByDate/')))
    except (ValueError,TypeError): return False

def index_entries(case_id, case_number, orders):
    result=[]
    for order in orders:
        if not official_pdf(order.get('officialUrl')): continue
        state={'Validated':'Processed','Unprocessed':'Unprocessed','NeedsSourceReview':'NeedsSourceReview'}.get(order.get('status'),'NeedsReview')
        result.append({'courtCaseId':case_id,'normalizedCaseIdentity':identity(case_number),
            'orderDate':order.get('orderDate'),'officialUrl':order['officialUrl'],
            'corrigendumUrl':order.get('corrigendumUrl'),'uploadDate':order.get('uploadDate'),
            'sourceObservationId':order.get('sourceObservationId'),'processingState':state,
            'sourceSha256':order.get('sha256'),'intelligenceVersion':order.get('versions'),
            'intelligenceArtifact':'current.json' if order.get('status')!='Unprocessed' else None,
            'processedAt':order.get('processedAt')})
    return result

def merge_known_orders(artifact, case_id, case_number, sources):
    if artifact and (artifact.get('caseId')!=case_id or identity(artifact.get('caseNumber',''))!=identity(case_number)):
        raise ValueError('Current-matter artifact/index identity mismatch')
    orders=list((artifact or {}).get('orders',[]))
    for source in sources:
        source_identity=source.get('normalizedCaseIdentity','')
        # The existing DB identity includes the forum. Do not equate other courts.
        if source_identity.startswith('delhihighcourt|') and len(source_identity.split('|'))==4:
            source_identity=''.join(source_identity.split('|')[1:])
        if source.get('courtCaseId')!=case_id or source_identity!=identity(case_number): continue
        day=source.get('orderDate')
        if not day or not official_pdf(source.get('officialUrl')): continue
        try: date.fromisoformat(day)
        except (ValueError,TypeError): continue
        prior=next((order for order in orders if order.get('orderDate')==day and order.get('officialUrl')==source['officialUrl']),None)
        metadata={key:source.get(key) for key in ('sourceObservationId','corrigendumUrl','uploadDate')}
        if prior: prior.update(metadata)
        else: orders.append(dict(officialUrl=source['officialUrl'],orderDate=day,caseNumber=case_number,
                                 status='Unprocessed',facts=[],sha256=None,**metadata))
    from semantics import synthesize
    result=synthesize(case_id,case_number,orders)
    result['processingComplete']=bool(orders) and all(order.get('status')!='Unprocessed' for order in orders)
    return result

@contextmanager
def pdf_lock(folder):
    # Shares the explicit worker's OS lock. Busy means fail calmly, never parallel PDFs.
    import msvcrt
    folder.mkdir(parents=True,exist_ok=True)
    with (folder/'.worker.lock').open('a+b') as lock:
        lock.seek(0)
        if not lock.read(1): lock.write(b'0'); lock.flush()
        lock.seek(0)
        msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
        try: yield
        finally:
            lock.seek(0); msvcrt.locking(lock.fileno(),msvcrt.LK_UNLCK,1)

def prepare_question(root, artifact, case_id, question, provider, processor=None):
    """Only a unique requested actual order may fetch. No render/timeline bulk fetch."""
    from anchors import CASE_REFERENCES
    if artifact.get('caseId')!=case_id: raise ValueError('Current matter mismatch')
    if re.search(r'other case|another case|across cases|all cases|dusre case|doosre case|दूसरे केस|सभी मामलों',question,re.I): return artifact
    if any(identity(match.group())!=identity(artifact.get('caseNumber','')) for match in CASE_REFERENCES.finditer(question)): return artifact
    target=requested_date(question,[order.get('orderDate') for order in artifact.get('orders',[])])
    if not target['requested'] or not target['date']: return artifact
    orders=artifact.get('orders',[])
    matches=[order for order in orders if order.get('orderDate')==target['date']]
    if len(matches)!=1: return artifact # Several publications on one date require source selection.
    source=matches[0]
    retry=bool(re.search(r'\b(?:refresh|retry|reprocess)\b|dobara process',question,re.I))
    if source.get('status')=='Validated' and not retry: return artifact # never redownload per question
    if source.get('status') in ('NeedsReview','NeedsSourceReview') and not retry: return artifact
    if source.get('status') not in ('Unprocessed','Validated','NeedsReview','NeedsSourceReview'): return artifact
    if not official_pdf(source.get('officialUrl')): return artifact
    from worker import process_order,atomic_json
    processor=processor or process_order
    folder=Path(root)/'court-intelligence'/'v1'
    case_folder=folder/str(uuid.UUID(case_id))
    with pdf_lock(folder):
        current=case_folder/'current.json'
        if current.is_file():
            if current.stat().st_size>2*1024*1024: raise ValueError('Artifact size limit')
            fresh=json.loads(current.read_text(encoding='utf-8'))
            artifact=merge_known_orders(fresh,case_id,artifact['caseNumber'],artifact.get('orderIndex',[]))
            orders=artifact['orders']
            matches=[order for order in orders if order.get('orderDate')==target['date']]
            if len(matches)!=1 or matches[0].get('status')=='Validated' and not retry: return artifact
            source=matches[0]
        class BoundedProvider:
            version=getattr(provider,'version','local')
            started=time.monotonic()
            def extract(self,*args,**kwargs):
                remaining=120-(time.monotonic()-self.started)
                if remaining<=0: raise ValueError('Lazy extraction time budget exceeded; source remains review work')
                previous=getattr(provider,'request_timeout',None)
                try:
                    if previous is not None: provider.request_timeout=min(previous,max(1,int(remaining)))
                    return provider.extract(*args,**kwargs)
                finally:
                    if previous is not None: provider.request_timeout=previous
        record=processor(source,artifact['caseNumber'],BoundedProvider())
        if source.get('sha256') and record.get('sha256') and source['sha256']!=record['sha256']:
            record.update(status='NeedsSourceReview',facts=[],failureMessage='Known official source bytes changed; explicit source-version review required')
        record.update({key:source.get(key) for key in ('sourceObservationId','corrigendumUrl','uploadDate')})
        updated=[record if order is source else order for order in orders]
        from semantics import synthesize
        result=synthesize(case_id,artifact['caseNumber'],updated)
        result['processingComplete']=all(order.get('status')!='Unprocessed' for order in updated)
        # Retain structured version history only; the processor deletes its temporary PDF.
        key=record.get('sha256') or 'unreadable'
        if not re.fullmatch(r'[a-f0-9]{64}|unreadable',key): raise ValueError('Invalid artifact digest')
        atomic_json(case_folder/'orders'/(target['date']+'-'+key+'.json'),record)
        atomic_json(case_folder/'current.json',result)
        return result
