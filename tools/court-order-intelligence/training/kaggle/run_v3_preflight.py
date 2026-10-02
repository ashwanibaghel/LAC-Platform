"""Private, bounded V3 hardware proof; no production endpoints/credentials."""
import json
import os
from pathlib import Path
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True

archives = list(Path('/kaggle/input').rglob('v3-preflight-bundle.zip'))
if len(archives) != 1:
    raise RuntimeError('Exactly one public-only preflight archive required')
bundle = Path('/kaggle/working/v3-preflight-bundle')
bundle.mkdir()
with zipfile.ZipFile(archives[0]) as archive:
    names = archive.namelist()
    if len(names) != len(set(names)):
        raise RuntimeError('Duplicate archive paths')
    for member in names:
        path = (bundle / member).resolve()
        if not path.is_relative_to(bundle) or path.suffix not in ('.py', '.json', '.jsonl', '.txt'):
            raise RuntimeError('Unsafe/forbidden upload archive path')
    archive.extractall(bundle)
sys.path.insert(0, str(bundle))
from v3_gpu_preflight import verify_bundle
verify_bundle(bundle)
subprocess.run(['nvidia-smi', '--query-gpu=name,memory.total,driver_version', '--format=csv,noheader'], check=True)
subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                '-r', str(bundle / 'requirements.txt')], check=True)
environment = dict(os.environ)
environment.update(CUDA_VISIBLE_DEVICES='0', PYTHONDONTWRITEBYTECODE='1', USE_TF='0', USE_FLAX='0')
subprocess.run([sys.executable, str(bundle / 'v3_gpu_preflight.py'), str(bundle),
               '/kaggle/working/v3-t4-preflight-proof'], env=environment, check=True)
