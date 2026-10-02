"""Private V2 kernel; mount public reviewed bundle and immutable V1 artifacts."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

matches = []
for path in Path('/kaggle/input').rglob('dataset-manifest.json'):
    if json.loads(path.read_text()).get('purpose') == ['PILOT_V2', 'BOUNDED_EVALUATION', 'NO_DEPLOYMENT']:
        matches.append(path)
if len(matches) != 1:
    raise RuntimeError('Exactly one frozen public-only V2 bundle required')
source = matches[0].parent.resolve()
manifest = json.loads(matches[0].read_text())
bundle = Path('/kaggle/working/lac-pilot-v2-bundle')
bundle.mkdir()
if (source / 'runtime.zip').is_file() and not (source / 'runtime').is_dir():
    with zipfile.ZipFile(source / 'runtime.zip') as archive:
        runtime = bundle / 'runtime'
        runtime.mkdir()
        for member in archive.infolist():
            if member.is_dir():
                continue
            target = (runtime / member.filename).resolve()
            if not target.is_relative_to(runtime) or Path(member.filename).name not in ('anchors.py', 'semantics.py'):
                raise RuntimeError('Unsafe runtime archive member')
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as incoming, target.open('wb') as outgoing:
                shutil.copyfileobj(incoming, outgoing)
for relative in ['dataset-manifest.json'] + list(manifest['files']):
    origin, target = (source / relative).resolve(), (bundle / relative).resolve()
    if not origin.is_relative_to(source) or not target.is_relative_to(bundle):
        raise RuntimeError('Unsafe manifest path')
    if not origin.is_file() and relative.startswith('runtime/') and target.is_file():
        continue
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(origin, target)
    if relative != 'dataset-manifest.json' and hashlib.sha256(target.read_bytes()).hexdigest() != manifest['files'][relative]:
        raise RuntimeError('Mount checksum failure before dependency install')
subprocess.run(['nvidia-smi', '--query-gpu=name,memory.total,driver_version', '--format=csv,noheader'], check=True)
subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check', '-r', str(bundle / 'requirements.txt')], check=True)
environment = dict(os.environ)
environment['CUDA_VISIBLE_DEVICES'] = '0'
subprocess.run([sys.executable, str(bundle / 'pilot_v2_train.py'), '--bundle', str(bundle),
    '--output', '/kaggle/working/lac-court-qwen4b-pilot-v2', '--ack-private-notebook'], check=True, env=environment)
