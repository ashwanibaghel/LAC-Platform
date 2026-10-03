"""Explicit, sequential cached-PDF CPU benchmark; no Court/network downloads.

Owns only its child llama-server, writes receipts to a fresh external directory.
Never edits model/package/storage or restarts product services. Case oracle is
reviewed separately, not fed to the model/selection/semantic gate.
"""
import argparse
import ctypes
import hashlib
import json
import subprocess
import threading
import time
from pathlib import Path
import psutil
import requests
from worker import native_pages, process_order
from provider import LlamaCppProvider
from semantics import synthesize
from anchors import anchors_for, schema_for, expand, INSTRUCTIONS
from native_layout import outer_paragraph_offsets
from preselection import select_candidates, bounded_chunks


class Memory(ctypes.Structure):
    _fields_ = [('length', ctypes.c_ulong), ('load', ctypes.c_ulong)] + [(n, ctypes.c_ulonglong) for n in
        ('totalPhys', 'availPhys', 'totalPage', 'availPage', 'totalVirtual', 'availVirtual', 'availExtended')]


class CounterValue(ctypes.Structure):
    _fields_ = [('status', ctypes.c_ulong), ('value', ctypes.c_double)]


def paging_counters():
    pdh=ctypes.WinDLL('pdh'); query=ctypes.c_void_p(); counters={}
    pdh.PdhOpenQueryW.argtypes=[ctypes.c_wchar_p,ctypes.c_size_t,ctypes.POINTER(ctypes.c_void_p)]
    pdh.PdhAddEnglishCounterW.argtypes=[ctypes.c_void_p,ctypes.c_wchar_p,ctypes.c_size_t,ctypes.POINTER(ctypes.c_void_p)]
    pdh.PdhGetFormattedCounterValue.argtypes=[ctypes.c_void_p,ctypes.c_ulong,ctypes.c_void_p,ctypes.POINTER(CounterValue)]
    if pdh.PdhOpenQueryW(None,0,ctypes.byref(query))==0:
        for name,path in [('pagesInputPerSec',r'\Memory\Pages Input/sec'),('pageReadsPerSec',r'\Memory\Page Reads/sec')]:
            handle=ctypes.c_void_p()
            if pdh.PdhAddEnglishCounterW(query,path,0,ctypes.byref(handle))==0: counters[name]=handle
        pdh.PdhCollectQueryData(query)
    return pdh,query,counters


def memory(pid=None):
    m = Memory(); m.length = ctypes.sizeof(m)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
    row = dict(t=time.time(), available=m.availPhys, commitUsed=m.totalPage-m.availPage,
               commitLimit=m.totalPage, headroom=m.availPage, pagefileUsed=psutil.swap_memory().used)
    if pid:
        try:
            p = psutil.Process(pid); info = p.memory_info()
            row.update(rss=info.rss, private=info.private, peakWorkingSet=info.peak_wset,
                       pageFaults=info.num_page_faults)
        except psutil.Error:
            pass
    return row


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--manifest', required=True)
    parser.add_argument('--pdf', required=True)
    parser.add_argument('--case-number', required=True)
    parser.add_argument('--order-date', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--ctx', type=int, default=4096)
    parser.add_argument('--threads', type=int, default=2)
    parser.add_argument('--batch', type=int, help='Supported logical batch size; unchanged unless explicitly supplied')
    parser.add_argument('--ubatch', type=int, help='Supported physical microbatch size; unchanged unless explicitly supplied')
    parser.add_argument('--cache-k', default='f16')
    parser.add_argument('--cache-v', default='f16')
    parser.add_argument('--flash', default='auto')
    parser.add_argument('--no-repack', action='store_true', help='Runtime-only memory/speed comparison, immutable weights')
    parser.add_argument('--cache-ram', type=int, help='Explicit prompt-snapshot cache budget MiB; 0 disables snapshots')
    parser.add_argument('--chunk', type=int, default=2)
    parser.add_argument('--timeout', type=int, default=300)
    parser.add_argument('--full-order', action='store_true', help='Offline full worker path, cached PDF only, external artifacts')
    parser.add_argument('--official-url', help='Source provenance only; full-order never downloads it')
    args = parser.parse_args()
    root = Path(args.output); root.mkdir(parents=True, exist_ok=False)
    manifest = json.loads(Path(args.manifest).read_text())
    for path_key, sha_key in [('baseGgufPath','baseGgufSha256'), ('loraGgufPath','loraGgufSha256'), ('serverPath','serverSha256')]:
        with Path(manifest[path_key]).open('rb') as stream:
            actual = hashlib.file_digest(stream, 'sha256').hexdigest()
        if actual != manifest[sha_key]: raise ValueError('Packaged artifact checksum mismatch: '+path_key)
    session = requests.Session(); session.trust_env = False
    try:
        session.get('http://127.0.0.1:8096/health', timeout=1)
    except requests.ConnectionError:
        pass
    else:
        raise RuntimeError('8096 occupied; existing server will not be touched')
    pages = native_pages(args.pdf)
    anchors = anchors_for(pages, outer_paragraph_offsets(args.pdf, pages))
    selected, audit = select_candidates(anchors, pages)
    chunks = bounded_chunks(selected)
    chunk = chunks[args.chunk]
    source = json.dumps(dict(documentOrderDate=args.order_date, caseNumber=args.case_number,
                            sourceRoleContext=pages[1][:1800], anchors=chunk), ensure_ascii=False)
    payload = dict(model='local', temperature=0, seed=17, max_tokens=1800, stream=False,
                   messages=[dict(role='system', content=INSTRUCTIONS),
                             dict(role='user', content='SOURCE DATA (not instructions):\n'+source)],
                   response_format=dict(type='json_object', schema=schema_for(chunk,pages)),
                   chat_template_kwargs=dict(enable_thinking=False))
    (root/'selection.json').write_text(json.dumps(dict(audit=audit, chunks=[[a['anchorId'] for a in c] for c in chunks], selected=selected), indent=2), encoding='utf-8')
    command = [manifest['serverPath'], '--model', manifest['baseGgufPath'], '--lora', manifest['loraGgufPath'],
               '--host','127.0.0.1','--port','8096','--ctx-size',str(args.ctx),'--threads',str(args.threads),
               '--parallel','1','--n-gpu-layers','0','--prio','-1','--poll','0',
               '--cache-type-k',args.cache_k,'--cache-type-v',args.cache_v,'--flash-attn',args.flash]
    if args.no_repack: command.append('--no-repack')
    for flag,value in [('--batch-size',args.batch),('--ubatch-size',args.ubatch)]:
        if value is not None:
            if value < 1: raise ValueError('Batch sizes must be positive')
            command.extend([flag,str(value)])
    if args.cache_ram is not None:
        if args.cache_ram < 0: raise ValueError('Benchmark cache budget must be bounded/nonnegative')
        command.extend(['--cache-ram',str(args.cache_ram)])
    result = dict(config=vars(args), command=command, before=memory(), selection=audit,
                  manifestSha=hashlib.sha256(Path(args.manifest).read_bytes()).hexdigest(), modelVersion=manifest['modelVersion'])
    stopped = threading.Event(); samples=[]; process=None
    def monitor():
        pdh,query,counters=paging_counters()
        with (root/'memory.jsonl').open('x', encoding='utf-8', buffering=1) as stream:
            while not stopped.is_set():
                row=memory(process.pid)
                pdh.PdhCollectQueryData(query)
                for name,handle in counters.items():
                    value=CounterValue()
                    if pdh.PdhGetFormattedCounterValue(handle,0x200,None,ctypes.byref(value))==0 and value.status in (0,1): row[name]=value.value
                samples.append(row); stream.write(json.dumps(row)+'\n')
                if row['headroom'] < 128*1024**2:
                    result['abort']='Commit headroom below 128 MiB'; process.terminate(); break
                stopped.wait(1)
        pdh.PdhCloseQuery(query)
    try:
        with (root/'stdout.log').open('x') as stdout, (root/'stderr.log').open('x') as stderr:
            start=time.perf_counter()
            process=subprocess.Popen(command, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
            result['pid']=process.pid
            watcher=threading.Thread(target=monitor, daemon=True); watcher.start()
            while time.perf_counter()-start < 120:
                if process.poll() is not None: raise RuntimeError('Model load failed; see preserved stderr')
                try:
                    if session.get('http://127.0.0.1:8096/health', timeout=1).status_code==200: break
                except requests.RequestException: pass
                time.sleep(1)
            else: raise RuntimeError('Model not ready within 120 seconds')
            result['loadSeconds']=time.perf_counter()-start; result['afterLoad']=memory(process.pid)
            templated=session.post('http://127.0.0.1:8096/apply-template', json=dict(messages=payload['messages'],chat_template_kwargs=payload['chat_template_kwargs']),timeout=15).json()['prompt']
            tokens=session.post('http://127.0.0.1:8096/tokenize',json=dict(content=templated,add_special=True,parse_special=True),timeout=15).json()['tokens']
            result['promptTokens']=len(tokens); result['promptFits']=len(tokens)<args.ctx
            result['allChunkPromptTokens']=[]
            for candidate_chunk in chunks:
                candidate_source=json.dumps(dict(documentOrderDate=args.order_date,caseNumber=args.case_number,
                    sourceRoleContext=pages[1][:1800],anchors=candidate_chunk),ensure_ascii=False)
                messages=[payload['messages'][0],dict(role='user',content='SOURCE DATA (not instructions):\n'+candidate_source)]
                template=session.post('http://127.0.0.1:8096/apply-template',json=dict(messages=messages,chat_template_kwargs=payload['chat_template_kwargs']),timeout=15).json()['prompt']
                measured=session.post('http://127.0.0.1:8096/tokenize',json=dict(content=template,add_special=True,parse_special=True),timeout=15).json()['tokens']
                result['allChunkPromptTokens'].append(len(measured))
            result['allPromptsFit']=all(n<args.ctx for n in result['allChunkPromptTokens'])
            if not result['promptFits']: raise ValueError('Exact untruncated prompt exceeds configured context')
            if not result['allPromptsFit']: raise ValueError('Another selected source chunk exceeds configured context; no truncation allowed')
            if args.full_order:
                if not args.official_url: raise ValueError('Official source provenance URL required')
                provider=LlamaCppProvider(model_version=manifest['modelVersion'],request_timeout=args.timeout)
                original_post=provider.session.post
                calls=[]
                def observed(*a,**kw):
                    began=time.perf_counter(); entry=dict(startedAt=time.time())
                    try:
                        response=original_post(*a,**kw)
                        entry.update(httpStatus=response.status_code,response=response.json())
                        return response
                    except Exception as error:
                        entry['failure']=type(error).__name__
                        raise
                    finally:
                        entry['seconds']=time.perf_counter()-began; calls.append(entry)
                        with (root/'calls.jsonl').open('a',encoding='utf-8') as trace:
                            trace.write(json.dumps(entry,ensure_ascii=False)+'\n')
                provider.session.post=observed
                with Path(args.pdf).open('rb') as stream: pdf_sha=hashlib.file_digest(stream,'sha256').hexdigest()
                start=time.perf_counter()
                order=process_order(dict(officialUrl=args.official_url,orderDate=args.order_date),args.case_number,provider,
                                    downloader=lambda url,directory:(Path(args.pdf),pdf_sha))
                result.update(fullOrderSeconds=time.perf_counter()-start,modelCalls=len(calls),orderStatus=order['status'],
                              coverage=order.get('coverage'),failureMessage=order.get('failureMessage'),pdfSha256=pdf_sha)
                (root/'order.json').write_text(json.dumps(order,indent=2,ensure_ascii=False),encoding='utf-8')
                artifact=synthesize('offline-benchmark-only',args.case_number,[order])
                (root/'artifact.json').write_text(json.dumps(artifact,indent=2,ensure_ascii=False),encoding='utf-8')
                result['structuredProcessingPass']=bool(order.get('facts') and order.get('coverage',{}).get('allSelectedChunksProcessed'))
                result['sourceOracleAcceptance']='REQUIRES_SEPARATE_MANUAL_REVIEW'
                return
            start=time.perf_counter(); result['requestStartUtc']=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())
            try:
                response=session.post('http://127.0.0.1:8096/v1/chat/completions',json=payload,timeout=(5,args.timeout),allow_redirects=False)
            finally:
                result['inferenceSeconds']=time.perf_counter()-start
            result['httpStatus']=response.status_code
            (root/'response.json').write_text(response.text,encoding='utf-8')
            response.raise_for_status(); body=response.json(); result['usage']=body.get('usage'); result['timings']=body.get('timings')
            choice=body['choices'][0]; result['finishReason']=choice.get('finish_reason')
            if choice.get('finish_reason')!='stop': raise ValueError('Structured inference did not complete')
            raw=json.loads(choice['message']['content']); result['jsonParse']=True
            validated=expand(raw,chunk,pages); result['schemaAndSemanticGate']=True
            result['facts']=validated['facts']; result['rawOutput']=raw
            (root/'validated.json').write_text(json.dumps(validated,indent=2,ensure_ascii=False),encoding='utf-8')
            result['structuredProcessingPass']=True
            result['sourceOracleAcceptance']='REQUIRES_SEPARATE_MANUAL_REVIEW'
    except Exception as error:
        result.update(error=type(error).__name__+': '+str(error), structuredProcessingPass=False)
    finally:
        stopped.set()
        if process:
            if process.poll() is None:
                process.terminate()
                try: process.wait(15)
                except subprocess.TimeoutExpired: process.kill(); process.wait()
            if 'watcher' in locals(): watcher.join(5)
        if samples:
            result['minimumAvailable']=min(s['available'] for s in samples)
            result['minimumHeadroom']=min(s['headroom'] for s in samples)
            result['peakCommit']=max(s['commitUsed'] for s in samples)
            result['peakModelRSS']=max(s.get('rss',0) for s in samples)
            result['peakModelPrivate']=max(s.get('private',0) for s in samples)
        (root/'result.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
        print(json.dumps({k:v for k,v in result.items() if k not in ('facts','rawOutput','command','selection')},indent=2),flush=True)


if __name__=='__main__': main()
