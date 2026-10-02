"""Private, bounded V3 hardware proof; no production endpoints/credentials."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

sys.dont_write_bytecode = True

def stage_bundle(inputs, bundle):
    """Kaggle expands ZIP uploads; accept that exact manifest inventory too."""
    archives = list(inputs.rglob('v3-preflight-bundle.zip'))
    manifests = list(inputs.rglob('dataset-manifest.json'))
    if len(archives) + len(manifests) != 1:
        raise RuntimeError('Exactly one public-only preflight bundle required')
    bundle.mkdir()
    if archives:
        with zipfile.ZipFile(archives[0]) as archive:
            names = archive.namelist()
            if len(names) != len(set(names)):
                raise RuntimeError('Duplicate archive paths')
            for member in names:
                path = (bundle / member).resolve()
                if not path.is_relative_to(bundle.resolve()) or path.suffix not in ('.py', '.json', '.jsonl', '.txt'):
                    raise RuntimeError('Unsafe/forbidden upload archive path')
            archive.extractall(bundle)
    else:
        # Copy only manifest-listed files, never unrelated mounted input data.
        root = manifests[0].parent
        manifest = json.loads(manifests[0].read_text())
        for name in ['dataset-manifest.json', *manifest['files']]:
            source = (root / name).resolve()
            target = (bundle / name).resolve()
            if (not source.is_relative_to(root.resolve()) or not target.is_relative_to(bundle.resolve())
                    or source.suffix not in ('.py', '.json', '.jsonl', '.txt')):
                raise RuntimeError('Unsafe/forbidden expanded bundle path')
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(source, target)


def main():
    bundle = Path('/kaggle/working/v3-preflight-bundle')
    stage_bundle(Path('/kaggle/input'), bundle)
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


if __name__ == '__main__':
    main()
