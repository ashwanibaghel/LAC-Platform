"""Immutable public TRAIN experiment; refuses failed/mismatched GPU proof."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

from prepare_v3_preflight import prepare, HERE, write_json
from v3_experiment_train import PURPOSE, PENDING_PURPOSE, verify_experiment


def prepare_experiment(destination, proof_path=None, preflight_manifest=None, integrated_preflight=False):
    if not integrated_preflight:
        proof = json.loads(proof_path.read_text())
        if proof.get('result') != 'PASS':
            raise ValueError('Successful real GPU preflight required before fit packaging')
    prepare(destination)
    bundle = destination / 'bundle'
    if not integrated_preflight:
        shutil.copyfile(proof_path, bundle / 'gpu-preflight-proof.json')
        shutil.copyfile(preflight_manifest, bundle / 'preflight-manifest.json')
    shutil.copyfile(HERE / 'v3_experiment_train.py', bundle / 'v3_experiment_train.py')
    manifest = json.loads((bundle / 'dataset-manifest.json').read_text())
    purpose = PENDING_PURPOSE if integrated_preflight else PURPOSE
    manifest.update(purpose=purpose, requires_gpu_proof=integrated_preflight,
                    full_v3_training_authorized=True, quality_acceptance_passed=False,
                    fresh_holdout_frozen=False, experiment_passes=3,
                    files={p.relative_to(bundle).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                           for p in bundle.rglob('*') if p.is_file() and p.name != 'dataset-manifest.json'})
    write_json(bundle / 'dataset-manifest.json', manifest)
    verify_experiment(bundle, pending_gate=integrated_preflight)
    # This newly created private staging ZIP only, never an old frozen artifact.
    with zipfile.ZipFile(destination / 'upload/v3-preflight-bundle.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(bundle.rglob('*')):
            if path.is_file():
                archive.write(path, path.relative_to(bundle).as_posix())
    write_json(destination / 'upload/dataset-metadata.json', dict(title='LAC V3 verified pool experimental training',
        id='ashwanibaghel9027/lac-v3-experiment-verified-public-gold', licenses=[dict(name='other')]))
    shutil.copyfile(HERE / 'run_v3_experiment.py', destination / 'kernel/run_v3_experiment.py')
    # stage helper is shipped in the bundle and next to the kernel entry point.
    shutil.copyfile(HERE / 'run_v3_preflight.py', bundle / 'run_v3_preflight.py')
    # Kaggle script has a single entry file: concatenate only the unchanged
    # stage helper source and entry function, with no early main() invocation.
    helper = (HERE / 'run_v3_preflight.py').read_text().split("if __name__ == '__main__':")[0]
    entry = (HERE / 'run_v3_experiment.py').read_text().replace('    from run_v3_preflight import stage_bundle\n', '')
    (destination / 'kernel/run_v3_experiment.py').write_text(helper + '\n' + entry)
    # Add copied helper to exact inventory after construction.
    manifest['files']['run_v3_preflight.py'] = hashlib.sha256((bundle / 'run_v3_preflight.py').read_bytes()).hexdigest()
    write_json(bundle / 'dataset-manifest.json', manifest)
    verify_experiment(bundle, pending_gate=integrated_preflight)
    with zipfile.ZipFile(destination / 'upload/v3-preflight-bundle.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(bundle.rglob('*')):
            if path.is_file():
                archive.write(path, path.relative_to(bundle).as_posix())
    write_json(destination / 'kernel/kernel-metadata.json', dict(id='ashwanibaghel9027/lac-pilot-v3-verified-pool-experiment',
        title='LAC Pilot V3 verified pool experiment', code_file='run_v3_experiment.py', language='python',
        kernel_type='script', is_private=True, enable_gpu=True, enable_internet=True,
        dataset_sources=['ashwanibaghel9027/lac-v3-experiment-verified-public-gold'],
        competition_sources=[], kernel_sources=[]))
    print(json.dumps(dict(purpose=purpose, path=str(destination), count=manifest['candidate_count'],
                         source_sha=manifest['source_sha'], quality_acceptance_passed=False)))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--proof', type=Path)
    parser.add_argument('--preflight-manifest', type=Path)
    parser.add_argument('--integrated-preflight', action='store_true')
    args = parser.parse_args()
    if not args.integrated_preflight and (args.proof is None or args.preflight_manifest is None):
        parser.error('Require proof + preflight manifest, or integrated mandatory preflight')
    prepare_experiment(args.output.resolve(), args.proof.resolve() if args.proof else None,
                       args.preflight_manifest.resolve() if args.preflight_manifest else None,
                       integrated_preflight=args.integrated_preflight)
