"""Private public-only Pilot launch. Never mounts a workbook or office data."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys

manifests = list(Path('/kaggle/input').rglob('dataset-manifest.json'))
if len(manifests) != 1:
    raise RuntimeError('Mount exactly one public-only Pilot bundle')
source = manifests[0].parent.resolve()
manifest = json.loads(manifests[0].read_text())
bundle = Path('/kaggle/working/lac-pilot-bundle')
bundle.mkdir()
for relative in ['dataset-manifest.json'] + list(manifest['files']):
    origin, target = (source / relative).resolve(), (bundle / relative).resolve()
    if not origin.is_relative_to(source) or not target.is_relative_to(bundle):
        raise RuntimeError('Unsafe manifest path')
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(origin, target)
subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                '-r', str(bundle / 'requirements.txt')], check=True)
environment = dict(os.environ)
environment['CUDA_VISIBLE_DEVICES'] = '0'
subprocess.run([sys.executable, str(bundle / 'pilot_train.py'), '--bundle', str(bundle),
                '--output', '/kaggle/working/lac-court-pilot-v1', '--ack-private-notebook'],
               check=True, env=environment)
