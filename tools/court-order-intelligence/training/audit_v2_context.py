"""CPU-only, offline pinned-tokenizer audit; never loads model weights.

Checks unchanged training and constrained-inference templates. Oversized
evidence is reported, never truncated or silently removed from gold.
"""
import ast
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
MODEL = 'Qwen/Qwen3-4B-Instruct-2507'
REVISION = 'cdbee75f17c01a7cc42f958dc650907174af0554'


def literal_instruction(path, name):
    # Read the existing exact constant without importing training dependencies
    # or starting any model/network operation in this isolated tokenizer env.
    for statement in ast.parse(path.read_text(encoding='utf-8')).body:
        if isinstance(statement, ast.Assign) and any(isinstance(t, ast.Name) and t.id == name for t in statement.targets):
            return ast.literal_eval(statement.value)
    raise ValueError('Unchanged instruction constant not found')


def messages(example):
    kind = 'anchors' if 'anchors' in example['input'] else 'claims'
    instructions = literal_instruction(ROOT.parent / 'anchors.py', 'INSTRUCTIONS') if kind == 'anchors' else literal_instruction(ROOT / 'kaggle/prepare_smoke_dataset.py', 'QA_INSTRUCTIONS')
    canonical = lambda v: json.dumps(v, ensure_ascii=False, sort_keys=True, separators=(',', ':'))
    return kind, [{'role': 'system', 'content': instructions},
                  {'role': 'user', 'content': canonical(example['input'])},
                  {'role': 'assistant', 'content': canonical(example['target'])}]


def check(tokenizer, example, schemas, cap=2048, output_allowance=512):
    kind, training = messages(example)
    prefix = tokenizer.apply_chat_template(training[:-1], tokenize=True, add_generation_prompt=True)
    full = tokenizer.apply_chat_template(training, tokenize=True, add_generation_prompt=False)
    if full[:len(prefix)] != prefix:
        raise ValueError('Assistant boundary is not a token prefix')
    inference = [dict(m) for m in training[:-1]]
    # Identical to reload_smoke.inference_messages; schema is only sent during
    # constrained evaluation. Training continues to use the original prompts.
    inference[0]['content'] += '\nExact output JSON Schema:\n' + json.dumps(schemas[kind], ensure_ascii=False, separators=(',', ':'))
    prompt = tokenizer.apply_chat_template(inference, tokenize=True, add_generation_prompt=True)
    return {'id': example['id'], 'task': example['task'], 'training_tokens': len(full),
            'inference_prompt_tokens': len(prompt), 'training_fits': len(full) <= cap,
            'inference_with_output_reserve_fits': len(prompt) + output_allowance <= cap}


def main():
    from transformers import AutoTokenizer
    import transformers
    if transformers.__version__ != '4.56.2':
        raise ValueError('Use the proven Kaggle tokenizer package version 4.56.2')
    frozen = ROOT / 'local-private/pilot-v1-frozen/lac-court-pilot-v1'
    metadata = json.loads((frozen / 'dataset-manifest.json').read_text())
    if (metadata['model'], metadata['revision']) != (MODEL, REVISION):
        raise ValueError('Frozen tokenizer base/revision mismatch')
    expected = json.loads((frozen / 'artifact-checksums.json').read_text())
    checked = {}
    for relative, sha in expected.items():
        if relative.startswith('adapter/') and not relative.endswith('.safetensors'):
            actual = hashlib.sha256((frozen / relative).read_bytes()).hexdigest()
            if sha != actual:
                raise ValueError('Frozen tokenizer artifact checksum differs')
            checked[relative] = actual
    tokenizer = AutoTokenizer.from_pretrained(frozen / 'adapter', local_files_only=True, trust_remote_code=False)
    schemas = json.loads((ROOT / 'pilot-v1/target.schemas.json').read_text(encoding='utf-8'))
    examples = [json.loads(line) for line in (ROOT / 'pilot-v2/train-foundation.jsonl').read_text(encoding='utf-8').splitlines()]
    results = [check(tokenizer, e, schemas) for e in examples]
    report = {'model': MODEL, 'revision': REVISION, 'tokenizer_version': transformers.__version__,
              'tokenizer_artifacts': checked, 'local_files_only': True, 'model_weights_loaded': False,
              'sequence_cap': 2048, 'inference_output_reserve': 512,
              'examples_checked': len(results), 'training_fit': sum(r['training_fits'] for r in results),
              'inference_reserve_fit': sum(r['inference_with_output_reserve_fits'] for r in results),
              'evidence_truncated': False, 'gpu_launch_allowed': False, 'results': results}
    (ROOT / 'pilot-v2/context-audit.json').write_text(json.dumps(report, indent=2, sort_keys=True) + '\n', encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k not in {'results', 'tokenizer_artifacts'}}, indent=2))


if __name__ == '__main__':
    main()
