"""Launch exactly one bounded experimental V3 fit on verified public TRAIN."""
import os
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True


def main():
    from run_v3_preflight import stage_bundle
    bundle = Path('/kaggle/working/v3-experiment-bundle')
    stage_bundle(Path('/kaggle/input'), bundle)
    sys.path.insert(0, str(bundle))
    from v3_experiment_train import verify_experiment, PURPOSE, PENDING_PURPOSE
    manifest = json.loads((bundle / 'dataset-manifest.json').read_text())
    pending = manifest['purpose'] == PENDING_PURPOSE
    verify_experiment(bundle, pending_gate=pending)
    subprocess.run(['nvidia-smi', '--query-gpu=name,memory.total,driver_version', '--format=csv,noheader'], check=True)
    subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                    '-r', str(bundle / 'requirements.txt')], check=True)
    environment = dict(os.environ)
    environment.update(CUDA_VISIBLE_DEVICES='0', PYTHONDONTWRITEBYTECODE='1', USE_TF='0', USE_FLAX='0')
    if pending:
        # Separate process: all proof model/optimizer CUDA allocations are gone
        # before loading the pristine base for the real full fit.
        from v3_gpu_preflight import PURPOSE as PREFLIGHT_PURPOSE
        preflight = Path('/kaggle/working/v3-mandatory-gpu-gate-bundle')
        shutil.copytree(bundle, preflight)
        initial = dict(manifest, purpose=PREFLIGHT_PURPOSE, full_v3_training_authorized=False)
        (preflight / 'dataset-manifest.json').write_text(json.dumps(initial, indent=2) + '\n')
        proof_dir = Path('/kaggle/working/v3-mandatory-gpu-gate-proof')
        subprocess.run([sys.executable, str(preflight / 'v3_gpu_preflight.py'), str(preflight),
                        str(proof_dir)], env=environment, check=True)
        # Never manufacture a successful proof. Strict verification below binds
        # the actual result, dataset/code hashes, CUDA resume and memory margin.
        shutil.copyfile(proof_dir / 'v3-preflight-result.json', bundle / 'gpu-preflight-proof.json')
        shutil.copyfile(preflight / 'dataset-manifest.json', bundle / 'preflight-manifest.json')
        manifest.update(purpose=PURPOSE, requires_gpu_proof=False)
        manifest['files'].update({name: hashlib.sha256((bundle / name).read_bytes()).hexdigest()
                                 for name in ('gpu-preflight-proof.json', 'preflight-manifest.json')})
        (bundle / 'dataset-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        verify_experiment(bundle)
    subprocess.run([sys.executable, str(bundle / 'v3_experiment_train.py'), str(bundle),
                   '/kaggle/working/lac-pilot-v3-experiment'], env=environment, check=True)


if __name__ == '__main__':
    main()
