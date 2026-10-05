"""Explicit application recovery of the pinned CPU pilot. No PDFs or inference."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time
from datetime import datetime,timezone
import requests
import psutil
from runtime_health import model_state,root_fingerprint

def digest(path):
    h=hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
    return h.hexdigest()

def package_digest(folder):
    h=hashlib.sha256()
    for p in sorted(Path(folder).glob('*.py')):
        h.update(p.name.encode());h.update(bytes.fromhex(digest(p)))
    return h.hexdigest()

def verify(manifest,expected,package_expected,root):
    if not Path(root).is_absolute() or not Path(root).is_dir():raise ValueError('CanonicalRootUnavailable')
    if digest(manifest)!=expected:raise ValueError('ManifestHashMismatch')
    if package_digest(Path(__file__).parent)!=package_expected:raise ValueError('QuestionPackageHashMismatch')
    m=json.loads(Path(manifest).read_text())
    if (m.get('pilotVersion')!='V3' or m.get('cloudFallback') is not False or m.get('parallel')!=1
        or m.get('gpuLayers')!=0 or m.get('modelEndpoint')!='http://127.0.0.1:8096'
        or m.get('questionEndpoint')!='http://127.0.0.1:8097'):raise ValueError('PinnedRuntimePolicyMismatch')
    for p,k in [('baseGgufPath','baseGgufSha256'),('loraGgufPath','loraGgufSha256'),('serverPath','serverSha256')]:
        if not Path(m[p]).is_absolute() or digest(m[p])!=m[k]:raise ValueError('ModelPackageHashMismatch')
    for name,sha in m['runtimeFileSha256'].items():
        if Path(name).name!=name or digest(Path(m['serverPath']).parent/name)!=sha:raise ValueError('RuntimePackageHashMismatch')
    return m

def main():
    p=argparse.ArgumentParser()
    for key in ['manifest','manifest-sha256','package-sha256','extraction-root','runtime-directory']:p.add_argument('--'+key,required=True)
    a=p.parse_args();folder=Path(a.runtime_directory)
    if not folder.is_absolute():raise SystemExit('Absolute runtime directory required')
    folder.mkdir(parents=True,exist_ok=True)
    # Hold an OS lock across verification/startup; separate API instances cannot launch duplicates.
    from order_index import pdf_lock
    guard=pdf_lock(folder)
    try:guard.__enter__()
    except OSError:return 0
    state=dict(runtimeState='Starting',reasonCode='PackageVerification',startedAt=datetime.now(timezone.utc).isoformat(),recoveryPid=os.getpid())
    def save():
        # Runtime lifecycle record is separate from the existing case refresh sidecar.
        tmp=folder/'recovery.tmp';tmp.write_text(json.dumps(state));tmp.replace(folder/'recovery.json')
    save()
    try:
        m=verify(a.manifest,a.manifest_sha256,a.package_sha256,a.extraction_root)
        state.update(modelVersion=m['modelVersion'],manifestSha256=a.manifest_sha256,
            questionPackageSha256=a.package_sha256,extractionRootFingerprint=root_fingerprint(a.extraction_root))
        def launch(name,command):
            with (folder/(name+'.stdout.log')).open('ab')as stdout,(folder/(name+'.stderr.log')).open('ab')as stderr:
                env=os.environ.copy();env['TEMP']=env['TMP']=str(folder);env['PYTHONNOUSERSITE']='1'
                child=subprocess.Popen(command,env=env,cwd=Path(__file__).parent,stdout=stdout,stderr=stderr,
                    creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
            state[name+'Pid']=child.pid;state[name+'StartedAt']=datetime.now(timezone.utc).isoformat();save()
            return child
        # Cached grounded Q&A can operate without inference; recover it independently.
        session=requests.Session();session.trust_env=False
        def question_ready():
            try:
                with session.get('http://127.0.0.1:8097/health',timeout=4,allow_redirects=False,stream=True)as r:
                    data=r.raw.read(8193)
                    if len(data)>8192 or r.status_code!=200:return False
                    health=json.loads(data)
                if (health.get('extractionRootFingerprint')!=state['extractionRootFingerprint']
                    or health.get('modelVersion')!=m['modelVersion'] or health.get('questionPackageSha256')!=a.package_sha256):
                    raise ValueError('ExistingQuestionRuntimeMismatch')
                return True
            except requests.RequestException:return False
        if not question_ready():
            child=launch('question',[sys.executable,str(Path(__file__).parent/'serve_questions.py'),
                '--extraction-root',a.extraction_root,'--model-version',m['modelVersion'],'--require-model-health',
                '--refresh-request-timeout-seconds','600','--refresh-case-timeout-seconds','1800'])
            for _ in range(30):
                if child.poll() is not None:raise ValueError('QuestionServiceStartFailed')
                if question_ready():break
                time.sleep(1)
            else:raise ValueError('QuestionServiceStartTimeout')
        state['questionServiceState']='Ready';save()
        if model_state()=='ModelOffline':
            command=[m['serverPath'],'--model',m['baseGgufPath'],'--lora',m['loraGgufPath'],
                '--host','127.0.0.1','--port','8096','--ctx-size','3072','--threads','6','--parallel','1','--n-gpu-layers','0',
                '--prio','-1','--poll','0','--cache-type-k','f16','--cache-type-v','f16','--flash-attn','auto',
                '--ubatch-size','128','--cache-ram','0']
            child=launch('model',command)
            for _ in range(120):
                if child.poll() is not None:
                    with (folder/'model.stderr.log').open('rb')as log:
                        log.seek(max(0,log.seek(0,2)-8192));tail=log.read().decode(errors='replace')
                    raise ValueError('ModelInsufficientMemory' if 'unable to allocate CPU_REPACK buffer' in tail else 'ModelStartFailed')
                if model_state()=='Ready':break
                time.sleep(1)
            else:raise ValueError('ModelStartTimeout')
        else:
            owners={c.pid for c in psutil.net_connections(kind='tcp') if c.laddr and c.laddr.port==8096 and c.status=='LISTEN'}
            if len(owners)!=1:raise ValueError('ExistingModelRuntimeMismatch')
            existing=psutil.Process(owners.pop());command=existing.cmdline()
            if Path(existing.exe()).resolve()!=Path(m['serverPath']).resolve():raise ValueError('ExistingModelRuntimeMismatch')
            for flag,value in [('--model',m['baseGgufPath']),('--lora',m['loraGgufPath']),('--host','127.0.0.1'),
                               ('--port','8096'),('--ctx-size','3072'),('--threads','6'),('--parallel','1'),('--n-gpu-layers','0')]:
                if flag not in command or command[command.index(flag)+1]!=value:raise ValueError('ExistingModelRuntimeMismatch')
        if model_state()!='Ready':raise ValueError('ModelNotReady')
        state.update(runtimeState='Ready',reasonCode='VerifiedServicesReady')
    except Exception as e:
        allowed={'CanonicalRootUnavailable','ManifestHashMismatch','QuestionPackageHashMismatch','PinnedRuntimePolicyMismatch','ExistingModelRuntimeMismatch',
            'ModelPackageHashMismatch','RuntimePackageHashMismatch','ModelStartFailed','ModelInsufficientMemory','ModelStartTimeout','ModelNotReady',
            'ExistingQuestionRuntimeMismatch','QuestionServiceStartFailed','QuestionServiceStartTimeout'}
        state.update(runtimeState='Failed',reasonCode=str(e) if str(e) in allowed else 'RecoveryFailed')
        state['message']='Insufficient machine memory for the accepted CPU model. Save and close unused heavy apps, then retry recovery.' if state['reasonCode']=='ModelInsufficientMemory' else 'Verified runtime recovery failed; inspect the safe reason code.'
    finally:
        state['completedAt']=datetime.now(timezone.utc).isoformat();save();guard.__exit__(None,None,None)
    return 0 if state['runtimeState']=='Ready' else 1

if __name__=='__main__':sys.exit(main())
