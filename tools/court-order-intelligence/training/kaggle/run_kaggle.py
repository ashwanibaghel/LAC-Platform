"""Private Kaggle launcher: attach only the prepared public smoke ZIP dataset."""
from pathlib import Path
import json
import os
import subprocess
import shutil
import sys
import zipfile

bundle = Path('/kaggle/working/lac-t2-bundle')
bundle.mkdir()
archives = list(Path('/kaggle/input').rglob('lac-t2-public-smoke.zip'))
if archives:
    if len(archives) != 1:
        raise RuntimeError('Ambiguous smoke archive')
    with zipfile.ZipFile(archives[0]) as package:
        for member in package.infolist():
            if not (bundle / member.filename).resolve().is_relative_to(bundle.resolve()):
                raise RuntimeError('Unsafe archive path')
        package.extractall(bundle)
else:
    # Kaggle expands uploaded dataset ZIPs into the mounted input directory.
    manifests = list(Path('/kaggle/input').rglob('dataset-manifest.json'))
    if len(manifests) != 1:
        raise RuntimeError('Public smoke dataset not mounted uniquely')
    source = manifests[0].parent
    metadata = json.loads(manifests[0].read_text())
    for relative in ['dataset-manifest.json'] + list(metadata['files']):
        origin, target = (source / relative).resolve(), (bundle / relative).resolve()
        if not origin.is_relative_to(source) or not target.is_relative_to(bundle):
            raise RuntimeError('Unsafe manifest path')
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(origin, target)
subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                '-r', str(bundle / 'requirements.txt')], check=True)
# Fresh Python process: no stale torch/module imports after dependency install.
environment = dict(os.environ)
# The smoke contract is batch=1 on one device, even on dual-T4 Kaggle hosts.
environment['CUDA_VISIBLE_DEVICES'] = '0'
subprocess.run([sys.executable, str(bundle / 'train_smoke.py'), '--bundle', str(bundle),
                '--output', '/kaggle/working/lac-t2-smoke', '--ack-private-notebook'],
               check=True, env=environment)
