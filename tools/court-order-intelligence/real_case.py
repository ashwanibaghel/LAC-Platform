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
from order_index import merge_known_orders, pdf_lock, MAX_CASE_ARTIFACT_BYTES, MAX_ORDER_ARTIFACT_BYTES
from semantics import identity, synthesize
from worker import process_order, atomic_json


def read_artifact(root, case_id, case_number=None):
    case_id=str(uuid.UUID(case_id))
    path=Path(root)/'court-intelligence'/'v1'/case_id/'current.json'
    if not path.is_file(): return None
    if path.stat().st_size>MAX_CASE_ARTIFACT_BYTES: raise ValueError('Artifact size limit')
    artifact=json.loads(path.read_text(encoding='utf-8'))
    if (artifact.get('version')!=1 or artifact.get('caseId')!=case_id
            or case_number is not None and identity(artifact.get('caseNumber',''))!=identity(case_number)
            or not isinstance(artifact.get('orders'),list)):
        raise ValueError('Registered-case artifact identity/shape mismatch')
    return artifact


def refresh_case(root, case_id, case_number, sources, provider, processor=process_order, progress=None,
                 timeout_seconds=900, on_order_started=None):
    if not isinstance(timeout_seconds, int) or not 1 <= timeout_seconds <= 1800:
        raise ValueError('Explicit refresh budget must be between 1 and 1800 seconds')
    case_id=str(uuid.UUID(case_id))
    folder=Path(root)/'court-intelligence'/'v1'
    case_folder=folder/case_id
    with pdf_lock(folder):
        existing=read_artifact(root,case_id,case_number)
        indexed=merge_known_orders(existing,case_id,case_number,sources,strict_index=True)
        if not indexed['orders']: raise ValueError('No exact dated official sources')
        records=list(indexed['orders']); reviews=0
        from semantics import VERSION
        expected_versions={'extraction':VERSION,'rulebook':'2','model':getattr(provider,'version','local')}
        def publish():
            result=synthesize(case_id,case_number,records)
            result.update(processingComplete=all(r.get('status') not in ('Unprocessed','Processing') and r.get('deepProcessingComplete') is not False for r in records),
                          processedAt=datetime.now(timezone.utc).isoformat())
            for r in records:
                if r.get('refreshFailure'):
                    result['sourceCoverage']['gaps'].append(dict(orderDate=r['orderDate'],
                        officialUrl=r['officialUrl'],reason=r['refreshFailure']))
            if len(json.dumps(result,ensure_ascii=False,indent=2).encode('utf-8'))>MAX_CASE_ARTIFACT_BYTES:
                raise ValueError('Structured case snapshot exceeds reader safety limit')
            atomic_json(case_folder/'current.json',result)
            return result
        class BoundedProvider:
            version=getattr(provider,'version','local')
            def extract(self,*args,**kwargs):
                remaining=deadline-time.monotonic()
                if remaining<=0: raise ValueError('Local order processing time budget ended')
                previous=getattr(provider,'request_timeout',None)
                try:
                    if previous is not None: provider.request_timeout=min(previous,max(1,int(remaining)))
                    return provider.extract(*args,**kwargs)
                finally:
                    if previous is not None: provider.request_timeout=previous
        def reusable(source):
            same_publication=(source.get('sourceEvidenceSha256')==source.get('processedEvidenceSha256')
                if source.get('sourceEvidenceSha256') and source.get('processedEvidenceSha256')
                else bool(source.get('sourceObservationId') and source.get('processedObservationId')==source.get('sourceObservationId')))
            complete=(source.get('status')=='Validated' or source.get('status')=='NeedsReview'
                and source.get('coverage',{}).get('allSelectedChunksProcessed') and not source.get('failureMessage')
                and source.get('deepProcessingComplete') is not False)
            blocked=source.get('status')=='NeedsSourceReview'
            return bool((complete or blocked) and not source.get('refreshFailure') and source.get('sha256')
                and source.get('versions')==expected_versions and same_publication)

        def persisted(source):
            digest=hashlib.sha256((source['officialUrl']+str(source['orderDate'])).encode()).hexdigest()
            return case_folder/'orders'/(digest+'.json')

        # Recover durable per-order records before scheduling. Cache reads do not
        # download any PDF or invoke the model, including reviewed complete briefs.
        for index,source in enumerate(records):
            path=persisted(source)
            if not reusable(source) and path.is_file() and path.stat().st_size<=MAX_ORDER_ARTIFACT_BYTES:
                cached=json.loads(path.read_text(encoding='utf-8'))
                if (cached.get('courtCaseId')==case_id and identity(cached.get('caseNumber',''))==identity(case_number)
                    and cached.get('officialUrl')==source['officialUrl'] and cached.get('orderDate')==source['orderDate']):
                    cached=dict(cached,**{key:source.get(key) for key in ('sourceObservationId','sourceEvidenceSha256')})
                    if reusable(cached): records[index]=cached
        completed={index for index,source in enumerate(records) if reusable(source)}
        schedule=sorted((index for index in range(len(records)) if index not in completed),
            key=lambda index:(records[index]['orderDate'],records[index]['officialUrl']),reverse=True)

        def store(index,record):
            candidate_records=list(records);candidate_records[index]=record
            candidate=synthesize(case_id,case_number,candidate_records)
            if len(json.dumps(candidate,ensure_ascii=False,indent=2).encode('utf-8'))>MAX_CASE_ARTIFACT_BYTES:
                raise ValueError('Structured case snapshot exceeds reader safety limit')
            if len(json.dumps(record,ensure_ascii=False,indent=2).encode('utf-8'))>MAX_ORDER_ARTIFACT_BYTES:
                raise ValueError('Structured order exceeds safety limit')
            atomic_json(persisted(record),record)
            records[index]=record
            return publish()

        def finish_record(source,record):
            if record.get('officialUrl')!=source['officialUrl'] or record.get('orderDate')!=source['orderDate']:
                raise ValueError('Processor returned another source/date')
            failed_without_facts=bool(source.get('failureMessage') and not source.get('facts'))
            if failed_without_facts and source.get('sha256'): record['previousAttemptSha256']=source['sha256']
            if source.get('sha256') and record.get('sha256') and source['sha256']!=record['sha256'] and not failed_without_facts:
                record.update(status='NeedsSourceReview',facts=[],sourceReasonCode='SourceBytesChanged',failureMessage='Known official source bytes changed; source-version review required')
            record.update({key:source.get(key) for key in ('courtCaseId','normalizedCaseIdentity','sourceObservationId','corrigendumUrl','uploadDate','sourceEvidenceSha256','sourceKind')})
            record.update(caseNumber=case_number,processedObservationId=source.get('sourceObservationId'),
                processedEvidenceSha256=source.get('sourceEvidenceSha256'),processedAt=datetime.now(timezone.utc).isoformat())
            from semantics import usable_facts
            if record.get('failureMessage') and usable_facts(source):
                return dict(source,refreshFailure='Latest source check failed; previously verified evidence retained.')
            if record.get('failureMessage'): record['facts']=[]
            # A smaller first brief must not replace a richer verified artifact
            # of the same checked bytes/version. Enrichment preserves its facts.
            if (usable_facts(source) and record.get('sha256')==source.get('sha256')
                and record.get('versions')==source.get('versions') and not record.get('failureMessage')):
                if record.get('deepProcessingComplete') is False and len(usable_facts(record))<len(usable_facts(source)):
                    return source
                record['facts']=list({json.dumps(f,sort_keys=True):f for f in source.get('facts',[])+record.get('facts',[])}.values())
            return record

        result=indexed
        for index in schedule:
            source=records[index]
            if on_order_started: on_order_started(source['orderDate'],len(completed),len(records))
            deadline=time.monotonic()+timeout_seconds
            def fast_ready(partial):
                partial=finish_record(source,partial)
                if not partial.get('failureMessage'):
                    store(index,partial)
                    if progress: progress(len(completed)+1,len(records),sum(bool(r.get('refreshFailure')) or r.get('status') in ('NeedsReview','NeedsSourceReview') for r in records))
            if processor is process_order:
                record=processor(source,case_number,BoundedProvider(),fast=index==schedule[0],
                    on_fast_ready=fast_ready if index==schedule[0] else None)
            else:
                record=processor(source,case_number,BoundedProvider())
            record=finish_record(source,record)
            result=store(index,record);completed.add(index)
            reviews=sum(bool(r.get('failureMessage') or r.get('refreshFailure')) or r.get('status') in ('NeedsReview','NeedsSourceReview') for r in records)
            if progress: progress(len(completed),len(records),reviews)
        if not schedule:
            result=publish()
            reviews=sum(bool(r.get('refreshFailure')) or r.get('status') in ('NeedsReview','NeedsSourceReview') for r in records)
            if progress: progress(len(records),len(records),reviews)
        return result,reviews


class RefreshController:
    def __init__(self,root,provider_factory,timeout_seconds=900):
        if not isinstance(timeout_seconds, int) or not 1 <= timeout_seconds <= 1800:
            raise ValueError('Explicit refresh budget must be between 1 and 1800 seconds')
        self.timeout_seconds=timeout_seconds
        self.root=Path(root); self.provider_factory=provider_factory
        self.lock=threading.Lock()
        self.active_case_id=None
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
        self.active_case_id=case_id
        status_path=self.root/'court-intelligence'/'v1'/case_id/'refresh.json'
        state=dict(caseId=case_id,status='Running',checked=0,total=len(merge_known_orders(existing,case_id,case_number,sources,strict_index=True)['orders']),needsReview=0,
                   startedAt=datetime.now(timezone.utc).isoformat())
        try:
            atomic_json(status_path,state)
            def run():
                try:
                    def progress(checked,total,reviews):
                        state.update(checked=checked,total=total,needsReview=reviews)
                        atomic_json(status_path,state)
                    def started(day,checked,total):
                        state.update(processingCurrentOrderDate=day,checked=checked,total=total)
                        atomic_json(status_path,state)
                    _,reviews=refresh_case(self.root,case_id,case_number,sources,self.provider_factory(),progress=progress,
                                           timeout_seconds=self.timeout_seconds,on_order_started=started)
                    state.update(status='CompletedWithReview' if reviews else 'Completed',processingCurrentOrderDate=None)
                except Exception:
                    # Provider errors can contain URLs; status never stores raw
                    # exception strings, prompts, questions or credentials.
                    state.update(status='Failed',message='Local processing stopped. Completed orders are saved; resume to process remaining orders.')
                finally:
                    try:
                        state['completedAt']=datetime.now(timezone.utc).isoformat()
                        atomic_json(status_path,state)
                    finally:
                        self.active_case_id=None
                        self.lock.release()
            threading.Thread(target=run,name='court-one-case-refresh',daemon=True).start()
        except BaseException:
            self.active_case_id=None
            self.lock.release(); raise
        return dict(caseId=case_id,status='Running')
