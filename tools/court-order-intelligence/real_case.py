"""Explicit registered-case refresh, existing index only; no discovery or DB.

One OS-held PDF/worker lock for the complete case. Publish a complete structured
snapshot atomically; failure never replaces existing verified facts with partial
chunks. Runtime status is a tiny case-scoped sidecar, not a database job system.
"""
import hashlib
import json
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from order_index import merge_known_orders, pdf_lock
from semantics import identity, synthesize
from worker import process_order, atomic_json


def read_artifact(root, case_id, case_number=None):
    case_id=str(uuid.UUID(case_id))
    path=Path(root)/'court-intelligence'/'v1'/case_id/'current.json'
    if not path.is_file(): return None
    if path.stat().st_size>2*1024*1024: raise ValueError('Artifact size limit')
    artifact=json.loads(path.read_text(encoding='utf-8'))
    if (artifact.get('version')!=1 or artifact.get('caseId')!=case_id
            or case_number is not None and identity(artifact.get('caseNumber',''))!=identity(case_number)
            or not isinstance(artifact.get('orders'),list)):
        raise ValueError('Registered-case artifact identity/shape mismatch')
    return artifact


def refresh_case(root, case_id, case_number, sources, provider, processor=process_order, progress=None,
                 timeout_seconds=900):
    if not isinstance(timeout_seconds, int) or not 1 <= timeout_seconds <= 1800:
        raise ValueError('Explicit refresh budget must be between 1 and 1800 seconds')
    case_id=str(uuid.UUID(case_id))
    folder=Path(root)/'court-intelligence'/'v1'
    case_folder=folder/case_id
    with pdf_lock(folder):
        existing=read_artifact(root,case_id,case_number)
        indexed=merge_known_orders(existing,case_id,case_number,sources,strict_index=True)
        if not indexed['orders']: raise ValueError('No exact dated official sources')
        records=[]; reviews=0
        deadline=time.monotonic()+timeout_seconds # one explicit case, finite local refresh
        class BoundedProvider:
            version=getattr(provider,'version','local')
            def extract(self,*args,**kwargs):
                remaining=deadline-time.monotonic()
                if remaining<=0: raise ValueError('Local case refresh time budget ended')
                previous=getattr(provider,'request_timeout',None)
                try:
                    if previous is not None: provider.request_timeout=min(previous,max(1,int(remaining)))
                    return provider.extract(*args,**kwargs)
                finally:
                    if previous is not None: provider.request_timeout=previous
        for source in indexed['orders']:
            if time.monotonic()>=deadline: raise ValueError('Local case refresh time budget ended')
            record=processor(source,case_number,BoundedProvider())
            if record.get('officialUrl')!=source['officialUrl'] or record.get('orderDate')!=source['orderDate']:
                raise ValueError('Processor returned another source/date')
            if source.get('sha256') and record.get('sha256') and source['sha256']!=record['sha256']:
                record.update(status='NeedsSourceReview',facts=[],failureMessage='Known official source bytes changed; source-version review required')
            record.update({key:source.get(key) for key in ('courtCaseId','normalizedCaseIdentity','sourceObservationId','corrigendumUrl','uploadDate')})
            if record.get('failureMessage') or record.get('status') in ('NeedsReview','NeedsSourceReview'):
                reviews+=1
            if record.get('failureMessage') and source.get('status')=='Validated':
                # Retain prior evidence, explicitly disclose the unsuccessful
                # refresh; never mark the new unreadable source as validated.
                record=dict(source,refreshFailure='Latest source check failed; previously verified evidence retained.')
            elif record.get('failureMessage'):
                record['facts']=[]
            records.append(record)
            digest=hashlib.sha256((source['officialUrl']+str(source['orderDate'])).encode()).hexdigest()
            atomic_json(case_folder/'orders'/(digest+'.json'),record)
            if progress: progress(len(records),len(indexed['orders']),reviews)
        result=synthesize(case_id,case_number,records)
        result.update(processingComplete=True,processedAt=datetime.now(timezone.utc).isoformat())
        for record in records:
            if record.get('refreshFailure'):
                result['sourceCoverage']['gaps'].append(dict(orderDate=record['orderDate'],
                    officialUrl=record['officialUrl'],reason=record['refreshFailure']))
        if len(json.dumps(result,ensure_ascii=False,indent=2).encode('utf-8'))>2*1024*1024:
            raise ValueError('Structured case snapshot exceeds reader safety limit')
        atomic_json(case_folder/'current.json',result)
        return result,reviews


class RefreshController:
    def __init__(self,root,provider_factory,timeout_seconds=900):
        if not isinstance(timeout_seconds, int) or not 1 <= timeout_seconds <= 1800:
            raise ValueError('Explicit refresh budget must be between 1 and 1800 seconds')
        self.timeout_seconds=timeout_seconds
        self.root=Path(root); self.provider_factory=provider_factory
        self.lock=threading.Lock()
        # Restart never resumes downloads. Disclose lost runtime work only.
        folder=self.root/'court-intelligence'/'v1'
        if folder.is_dir():
            for path in folder.glob('*/refresh.json'):
                if path.stat().st_size>8192: continue
                try:
                    state=json.loads(path.read_text(encoding='utf-8'))
                    if str(uuid.UUID(path.parent.name))!=state.get('caseId'): continue
                    if state.get('status')=='Running':
                        state.update(status='Interrupted',message='Local processing session ended; retry explicitly.')
                        atomic_json(path,state)
                except (ValueError,TypeError,KeyError): continue

    def start(self,case_id,case_number,sources):
        case_id=str(uuid.UUID(case_id))
        if not isinstance(case_number,str) or not case_number or not isinstance(sources,list) or len(sources)>1000:
            raise ValueError('Explicit bounded registered-case index required')
        # Validate scope and existence before starting any thread or model call.
        existing=read_artifact(self.root,case_id,case_number)
        if not merge_known_orders(existing,case_id,case_number,sources,strict_index=True)['orders']:
            raise ValueError('No exact official sources')
        if not self.lock.acquire(blocking=False): return None
        status_path=self.root/'court-intelligence'/'v1'/case_id/'refresh.json'
        state=dict(caseId=case_id,status='Running',checked=0,total=0,needsReview=0,
                   startedAt=datetime.now(timezone.utc).isoformat())
        try:
            atomic_json(status_path,state)
            def run():
                try:
                    def progress(checked,total,reviews):
                        state.update(checked=checked,total=total,needsReview=reviews)
                        atomic_json(status_path,state)
                    _,reviews=refresh_case(self.root,case_id,case_number,sources,self.provider_factory(),progress=progress,
                                           timeout_seconds=self.timeout_seconds)
                    state.update(status='CompletedWithReview' if reviews else 'Completed')
                except Exception:
                    # Provider errors can contain URLs; status never stores raw
                    # exception strings, prompts, questions or credentials.
                    state.update(status='Failed',message='Local intelligence check could not finish. Existing verified evidence is unchanged.')
                finally:
                    try:
                        state['completedAt']=datetime.now(timezone.utc).isoformat()
                        atomic_json(status_path,state)
                    finally: self.lock.release()
            threading.Thread(target=run,name='court-one-case-refresh',daemon=True).start()
        except BaseException:
            self.lock.release(); raise
        return dict(caseId=case_id,status='Running')
