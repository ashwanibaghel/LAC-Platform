"""Allowlisted public TRAIN-only candidate bundle for a six-step hardware proof.

Never a final freeze, never contains blind targets/private workbook/raw PDFs or
adapters. Existing V1/V2 upload artifacts are not reused or overwritten.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import zipfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(ROOT))
from foundation import canonical, write_json, write_jsonl
from v3_context import compact_input, restore_input
from v3_contract import parse_v3_output, target_schemas_v3
from audit_v3_composite import verify_frozen
from v3_gpu_preflight import PURPOSE, verify_bundle
from prepare_smoke_dataset import QA_INSTRUCTIONS
from anchors import INSTRUCTIONS


def prepare(destination):
    if destination.exists():
        raise ValueError('Fresh preflight directory required')
    if subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=no'], cwd=ROOT, text=True).strip():
        raise ValueError('Commit reviewed source before uploading an exact preflight SHA')
    frozen = verify_frozen()
    report = json.loads((ROOT / 'pilot-v3/targeted-gap-audit.json').read_text())
    if report['protected_exclusions'] or report['quarantine'] or not report['legacy_files_unchanged']:
        raise ValueError('Preflight must not include unresolved/unsafe gold')
    source = ROOT / 'pilot-v3/train-composite-candidate.jsonl'
    schemas = target_schemas_v3()
    data = []
    for line in source.read_text(encoding='utf-8').splitlines():
        example = json.loads(line)
        if example['state'] != 'VERIFIED_GOLD':
            raise ValueError('Non-gold candidate')
        value = compact_input(example['input'])
        if restore_input(value) != example['input']:
            raise ValueError('Lossless context roundtrip failed')
        row = {k: example[k] for k in ('id', 'matter_id', 'task', 'language', 'outcome', 'contract', 'leakage_group')}
        row.update(input=value, expected=example['target'])
        parse_v3_output(canonical(row['expected']), row, schemas, ROOT.parent)
        system = INSTRUCTIONS if row['contract'] == 'anchors' else QA_INSTRUCTIONS
        row['messages'] = [dict(role='system', content=system), dict(role='user', content=canonical(value)),
                           dict(role='assistant', content=canonical(row['expected']))]
        data.append(row)
    if len(data) != report['combined_counts']['total_examples']:
        raise ValueError('Audited candidate inventory mismatch')
    destination.mkdir(parents=True)
    bundle = destination / 'bundle'
    bundle.mkdir()
    write_jsonl(bundle / 'train.jsonl', data)
    write_json(bundle / 'target.schemas.json', schemas)
    for name in ('v3_gpu_preflight.py', 'smoke_contract.py', 'reload_smoke.py', 'requirements.txt'):
        shutil.copyfile(HERE / name, bundle / name)
    for name in ('v3_sampler.py', 'v3_trainer.py', 'v3_context.py', 'v3_contract.py'):
        shutil.copyfile(ROOT / name, bundle / name)
    for name in ('v3_semantics.py', 'semantic_gate.py', 'anchors.py', 'semantics.py', 'questions.py', 'query_intents.py'):
        shutil.copyfile(ROOT.parent / name, bundle / name)
    (bundle / 'schema').mkdir()
    shutil.copyfile(ROOT / 'schema/contracts.py', bundle / 'schema/contracts.py')
    files = {p.relative_to(bundle).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
             for p in bundle.rglob('*') if p.is_file()}
    write_json(bundle / 'dataset-manifest.json', dict(purpose=PURPOSE, private=True,
        source_sha=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
        candidate_count=len(data), files=files, raw_candidate_sha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        private_workbook_included=False, pdfs_included=False, weights_included=False,
        frozen_v1_v2_checksums=frozen, full_v3_training_authorized=False))
    verify_bundle(bundle)
    upload = destination / 'upload'
    upload.mkdir()
    with zipfile.ZipFile(upload / 'v3-preflight-bundle.zip', 'w', zipfile.ZIP_DEFLATED) as archive:
        for path in sorted(bundle.rglob('*')):
            if path.is_file():
                archive.write(path, path.relative_to(bundle).as_posix())
    write_json(upload / 'dataset-metadata.json', dict(title='LAC V3 bounded T4 preflight public gold',
        id='ashwanibaghel9027/lac-v3-t4-preflight-public-gold', licenses=[dict(name='other')]))
    kernel = destination / 'kernel'
    kernel.mkdir()
    shutil.copyfile(HERE / 'run_v3_preflight.py', kernel / 'run_v3_preflight.py')
    write_json(kernel / 'kernel-metadata.json', dict(id='ashwanibaghel9027/lac-v3-t4-curriculum-preflight',
        title='LAC V3 T4 Curriculum Preflight', code_file='run_v3_preflight.py', language='python',
        kernel_type='script', is_private=True, enable_gpu=True, enable_internet=True,
        dataset_sources=['ashwanibaghel9027/lac-v3-t4-preflight-public-gold'], competition_sources=[], kernel_sources=[]))
    print(json.dumps(dict(path=str(destination), count=len(data), purpose=PURPOSE,
                         zip_sha256=hashlib.sha256((upload / 'v3-preflight-bundle.zip').read_bytes()).hexdigest())))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, required=True)
    prepare(parser.parse_args().output.resolve())
