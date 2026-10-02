"""Launch exactly one bounded experimental V3 fit on verified public TRAIN."""
import os
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True


def main():
    from run_v3_preflight import stage_bundle
    bundle = Path('/kaggle/working/v3-experiment-bundle')
    stage_bundle(Path('/kaggle/input'), bundle)
    sys.path.insert(0, str(bundle))
    from v3_experiment_train import verify_experiment
    verify_experiment(bundle)
    subprocess.run(['nvidia-smi', '--query-gpu=name,memory.total,driver_version', '--format=csv,noheader'], check=True)
    subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                    '-r', str(bundle / 'requirements.txt')], check=True)
    environment = dict(os.environ)
    environment.update(CUDA_VISIBLE_DEVICES='0', PYTHONDONTWRITEBYTECODE='1', USE_TF='0', USE_FLAX='0')
    subprocess.run([sys.executable, str(bundle / 'v3_experiment_train.py'), str(bundle),
                   '/kaggle/working/lac-pilot-v3-experiment'], env=environment, check=True)


if __name__ == '__main__':
    main()
