"""Inference-only frozen officer comparison from completed trusted Pilot output."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys


def main():
    sources = [p.parent for p in Path('/kaggle/input').rglob('run-metadata.json')
               if p.parent.name == 'lac-court-pilot-v1']
    bundles = [p.parent for p in Path('/kaggle/input').rglob('dataset-manifest.json')
               if p.parent.name == 'lac-pilot-bundle']
    questions = list(Path('/kaggle/input').rglob('officer-evaluation.jsonl'))
    if len(sources) != 1 or len(bundles) != 1 or len(questions) != 1:
        raise RuntimeError('Exactly one completed Pilot output and frozen officer dataset required')
    source, original, question_file = sources[0], bundles[0], questions[0]
    if '--prepared' not in sys.argv:
        subprocess.run([sys.executable, '-m', 'pip', 'install', '--disable-pip-version-check',
                        '-r', str(original / 'requirements.txt')], check=True)
        environment = dict(os.environ)
        environment['CUDA_VISIBLE_DEVICES'] = '0'
        subprocess.run([sys.executable, __file__, '--prepared'], check=True, env=environment)
        return
    sys.path.insert(0, str(original))
    from pilot_train import evaluate, save
    from pilot_contract import verify_bundle
    verify_bundle(original)
    hashes = json.loads((source / 'artifact-checksums.json').read_text())
    metadata = json.loads((source / 'run-metadata.json').read_text())
    if not metadata['adapter_reload_passed'] or not metadata['resume_result']['passed']:
        raise RuntimeError('Training/reload proof required')
    package_metadata = json.loads(question_file.with_name('officer-manifest.json').read_text())
    if (hashlib.sha256(question_file.read_bytes()).hexdigest() != package_metadata['sha256']
            or not package_metadata['frozen_before_model_output_inspection']):
        raise RuntimeError('Frozen officer question integrity failed')
    bundle = Path('/kaggle/working/lac-officer-bundle')
    bundle.mkdir()
    for name in ('training_config_pilot.json', 'target.schemas.json'):
        shutil.copyfile(original / name, bundle / name)
    shutil.copytree(original / 'runtime', bundle / 'runtime')
    records = [json.loads(line) for line in question_file.read_text().splitlines()]
    splits = json.loads((original / 'splits.json').read_text())['matter_inventory']['matters']
    for split in ('validation', 'blind'):
        selected = [r for r in records if splits[r['matter_id']]['split'] == split]
        if not selected or any(r['training_eligible'] for r in selected):
            raise RuntimeError('Officer protected split validation failed')
        (bundle / (split + '.jsonl')).write_text(''.join(json.dumps(r, ensure_ascii=False) + '\n' for r in selected))
    output = Path('/kaggle/working/lac-pilot-officer-evaluation')
    output.mkdir()
    # Copy/check only our own trusted adapter/checkpoint files. No arbitrary pickle.
    for relative in ['run-metadata.json', 'checkpoints/checkpoint-80/optimizer.pt'] + [str(p.relative_to(source)) for p in (source / 'adapter').iterdir() if p.is_file()]:
        origin = source / relative
        if hashlib.sha256(origin.read_bytes()).hexdigest() != hashes[relative]:
            raise RuntimeError('Pilot artifact checksum mismatch')
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(origin, destination)
    evaluate(bundle, output)
    save(output / 'officer-proof.json', {'training_steps_this_kernel': 0,
        'frozen_question_sha256': package_metadata['sha256'],
        'adapter_sha256': hashes['adapter/adapter_model.safetensors'],
        'post_blind_tuning': False})
    save(output / 'artifact-checksums.json', {str(p.relative_to(output)): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in output.rglob('*') if p.is_file()})


if __name__ == '__main__':
    main()
