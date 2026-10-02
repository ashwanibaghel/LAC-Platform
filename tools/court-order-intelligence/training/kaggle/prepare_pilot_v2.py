"""Build a fresh allowlisted V2 upload from immutable split/checksum freeze."""
import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
from freeze_pilot_v2 import verify_frozen
from foundation import canonical, write_json, write_jsonl
from prepare_smoke_dataset import QA_INSTRUCTIONS
from anchors import INSTRUCTIONS
from schema.contracts import TASKS
from pilot_v2_contract import PURPOSE, verify_bundle


def serialize(e):
    kind = TASKS[e['task']]
    return {'id': e['id'], 'matter_id': e['matter_id'], 'task': e['task'],
            'language': e['language'], 'outcome': e['outcome'], 'contract': kind,
            'leakage_group': e['leakage_group'], 'input': e['input'], 'expected': e['target'],
            'messages': [{'role': 'system', 'content': INSTRUCTIONS if kind == 'anchors' else QA_INSTRUCTIONS},
                         {'role': 'user', 'content': canonical(e['input'])},
                         {'role': 'assistant', 'content': canonical(e['target'])}]}


def prepare(output):
    if output.exists():
        raise ValueError('Fresh upload path required; never replace V1/V2 freeze')
    source = HERE.parent / 'pilot-v2'
    frozen = verify_frozen(source)
    v1 = HERE.parent / 'local-private/pilot-upload-v1'
    old = json.loads((v1 / 'dataset-manifest.json').read_text())
    # Regression targets/messages must remain byte-for-byte frozen V1 records.
    for split in ('validation', 'blind'):
        if hashlib.sha256((v1 / (split + '.jsonl')).read_bytes()).hexdigest() != old['files'][split + '.jsonl']:
            raise ValueError('V1 regression artifact differs')
    output.mkdir(parents=True)
    counts = {}
    for split in ('train', 'validation', 'blind'):
        examples = [json.loads(line) for line in (source / (split + '.jsonl')).read_text(encoding='utf-8').splitlines()]
        write_jsonl(output / (split + '.jsonl'), [serialize(e) for e in examples])
        counts[split] = len(examples)
    regression = []
    for split in ('validation', 'blind'):
        for line in (v1 / (split + '.jsonl')).read_text(encoding='utf-8').splitlines():
            row = json.loads(line)
            row['original_v1_split'] = split
            regression.append(row)
    write_jsonl(output / 'v1_regression.jsonl', regression)
    counts['v1_regression'] = len(regression)
    for name in ('train_smoke.py', 'smoke_contract.py', 'reload_smoke.py', 'pilot_contract.py',
                 'pilot_train.py', 'pilot_v2_contract.py', 'pilot_v2_train.py', 'requirements.txt'):
        shutil.copyfile(HERE / name, output / name)
    config = json.loads((HERE / 'training_config_pilot.json').read_text())
    config.update({'purpose': PURPOSE, 'oom_sequence_lengths': [],
                   'representative_tasks': sorted({serialize(e)['task'] for e in [json.loads(x) for x in (source / 'train.jsonl').read_text(encoding='utf-8').splitlines()]})})
    write_json(output / 'training_config_pilot_v2.json', config)
    shutil.copyfile(HERE.parent / 'pilot-v1/target.schemas.json', output / 'target.schemas.json')
    shutil.copyfile(source / 'manifest.json', output / 'split-freeze-manifest.json')
    runtime = output / 'runtime'
    runtime.mkdir()
    for name in ('anchors.py', 'semantics.py'):
        shutil.copyfile(HERE.parent.parent / name, runtime / name)
    files = {p.relative_to(output).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in output.rglob('*') if p.is_file()}
    write_json(output / 'dataset-manifest.json', {'purpose': PURPOSE, 'private': True,
        'source_sha': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=HERE, text=True).strip(),
        'model': frozen['model'], 'revision': frozen['revision'], 'counts': counts, 'files': files,
        'protected_splits_frozen': True, 'split_freeze_sha256': hashlib.sha256((source / 'manifest.json').read_bytes()).hexdigest(),
        'private_workbook_included': False, 'weights_included': False, 'pdfs_included': False})
    verify_bundle(output)
    print(json.dumps({'bundle': str(output), 'counts': counts, 'verified': True}))


if __name__ == '__main__':
    cli = argparse.ArgumentParser()
    cli.add_argument('--output', type=Path, required=True)
    prepare(cli.parse_args().output.resolve())
