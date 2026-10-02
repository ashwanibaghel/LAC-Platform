"""Frozen V2-only bundle gate; no network, inference or credential access."""
import hashlib
import json
from pathlib import Path
from pilot_contract import verify_config as original_config
from smoke_contract import MODEL, REVISION

PURPOSE = ['PILOT_V2', 'BOUNDED_EVALUATION', 'NO_DEPLOYMENT']
V1_ADAPTER_SHA = '09ebbd176cbdf0390bee3e47343b351da7a2fe80c72a8837e7de7f6eb291fb67'


def verify_config(config):
    original_config(config)
    if config['purpose'] != PURPOSE or config['max_steps'] != 80 or config['oom_sequence_lengths'] != []:
        raise ValueError('One bounded V2 run; no shorter-context data dropping')
    return config


def verify_bundle(path):
    path = path.resolve()
    manifest = json.loads((path / 'dataset-manifest.json').read_text())
    if manifest['purpose'] != PURPOSE or not manifest['private'] or not manifest['protected_splits_frozen']:
        raise ValueError('Private frozen V2 bundle required')
    if (manifest['model'], manifest['revision']) != (MODEL, REVISION):
        raise ValueError('Pinned base mismatch')
    if manifest['private_workbook_included'] or manifest['weights_included'] or manifest['pdfs_included']:
        raise ValueError('Disallowed upload content')
    actual = {p.relative_to(path).as_posix() for p in path.rglob('*') if p.is_file()}
    if actual != set(manifest['files']) | {'dataset-manifest.json'}:
        raise ValueError('Bundle contains unlisted files')
    for relative, sha in manifest['files'].items():
        file = (path / relative).resolve()
        if not file.is_relative_to(path) or hashlib.sha256(file.read_bytes()).hexdigest() != sha:
            raise ValueError('V2 bundle integrity/path failure')
    sets = {s: [json.loads(line) for line in (path / (s + '.jsonl')).read_text().splitlines()]
            for s in ('train', 'validation', 'blind', 'v1_regression')}
    seen, group_split = set(), {}
    for split, rows in sets.items():
        if not rows or len(rows) != manifest['counts'][split]:
            raise ValueError('V2 count mismatch/empty set')
        matters = {r['matter_id'] for r in rows}
        if seen & matters:
            raise ValueError('Matter crossed protected boundary')
        seen |= matters
        for row in rows:
            if split == 'v1_regression':
                continue
            group = row['leakage_group']
            if group in group_split and group_split[group] != split:
                raise ValueError('Leakage group crosses frozen split')
            group_split[group] = split
    if len({r['id'] for rows in sets.values() for r in rows}) != sum(map(len, sets.values())):
        raise ValueError('Duplicate record ID')
    return sets['train'], manifest


def locate_v1_adapter(root):
    matches = [p for p in Path(root).rglob('adapter_model.safetensors')
               if p.parent.name == 'adapter' and hashlib.sha256(p.read_bytes()).hexdigest() == V1_ADAPTER_SHA]
    if len(matches) != 1:
        raise ValueError('Exactly one immutable frozen V1 adapter mount required')
    config = json.loads((matches[0].parent / 'adapter_config.json').read_text())
    if config['revision'] != REVISION:
        raise ValueError('V1 adapter revision differs')
    return matches[0].parent
