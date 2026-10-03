"""Offline, evaluated V3 LoRA conversion; V1 and the pinned Q4_K_M base are read-only.

This is a local acceptance package, NOT a promotion of the NO-GO V3 evaluation.
No model is launched and no Hugging Face/download request is made by this tool.
"""
import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import subprocess
import sys

from package_pilot_v1 import MODEL, REVISION, LLAMA_COMMIT, sha256, verify_converter
from finalize_pilot_v1 import verify_lora, write_manifest

V3_ADAPTER_SHA = 'd57f20ada9a5d07b9f472385393127ba6ae1a8191c7247441cd5473448965175'
V3_ARTIFACT_MANIFEST_SHA = '2f219f3567c52befbc528943b7352a7dc66a7c9055b27eee442d96b3c29de934'
BASE_SHA = '9657e9d21175ed290fa4ec3662fffb61a001463ac4ec1abd62deaa509b440710'
V1_DEPLOYMENT_SHA = 'd3fb70af9dd325e4e2a13eb8015ebc52208aadb6a370c103fb17f0798922204c'
BASE_CONFIG_SHA = '5beea1a4a34c62782bfb2f911c606741a3bab8f92d80a118fa053c28af12e8ba'


def verify_v3(root, evaluation):
    root, evaluation = Path(root).resolve(), Path(evaluation).resolve()
    if sha256(root/'artifact-checksums.json') != V3_ARTIFACT_MANIFEST_SHA:
        raise ValueError('Evaluated V3 artifact manifest changed')
    manifest = json.loads((root/'artifact-checksums.json').read_text('utf-8'))
    for name, expected in manifest.items():
        path = (root/name).resolve()
        if not path.is_relative_to(root) or sha256(path) != expected:
            raise ValueError(f'V3 artifact integrity mismatch: {name}')
    # Bind the preserved evaluation metadata to its own artifact receipt too.
    evaluation_manifest = json.loads((evaluation/'artifact-checksums.json').read_text('utf-8'))
    for name, expected in evaluation_manifest.items():
        path = (evaluation/name).resolve()
        if not path.is_relative_to(evaluation) or sha256(path) != expected:
            raise ValueError('V3 evaluation integrity mismatch')
    metadata = json.loads((root/'run-metadata.json').read_text('utf-8'))
    evaluated = json.loads((evaluation/'evaluation-metadata.json').read_text('utf-8'))
    config = json.loads((root/'adapter/adapter_config.json').read_text('utf-8'))
    if (sha256(root/'adapter/adapter_model.safetensors') != V3_ADAPTER_SHA
            or metadata.get('model_id') != MODEL or metadata.get('revision') != REVISION
            or not metadata.get('fit_complete') or not metadata.get('adapter_reload_passed')
            or metadata.get('purpose') != 'V3_EXPERIMENT_VERIFIED_POOL_NOT_QUALITY_ACCEPTANCE'
            or evaluated.get('adapter_sha256') != V3_ADAPTER_SHA
            or evaluated.get('base') != MODEL or evaluated.get('revision') != REVISION
            or config.get('base_model_name_or_path') != MODEL
            or config.get('revision') not in (None, REVISION)
            or config.get('r') != 8 or config.get('lora_alpha') != 16
            or set(config.get('target_modules', [])) != {'q_proj','k_proj','v_proj','o_proj'}
            or config.get('modules_to_save') is not None or config.get('use_dora')
            or config.get('use_rslora') or config.get('bias') != 'none'):
        raise ValueError('Not the preserved evaluated V3 / pinned Qwen3 adapter')
    # PEFT wrote revision:null; immutable fit AND evaluation receipts establish
    # the base revision. Never rewrite the preserved adapter config to hide it.
    return len(manifest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('v3-root','evaluation-root','v1-manifest','base','converter','base-config','output-root'):
        parser.add_argument('--'+name, type=Path, required=True)
    args = parser.parse_args()
    output = args.output_root.resolve()
    v1_root = args.v1_manifest.resolve().parent
    if output.is_relative_to(v1_root) or output.exists():
        raise ValueError('Use a NEW separate V3 package directory; V1 must remain intact')
    verified_files = verify_v3(args.v3_root, args.evaluation_root)
    verify_converter(args.converter)
    if sha256(args.v1_manifest) != V1_DEPLOYMENT_SHA:
        raise ValueError('Verified V1 deployment manifest changed')
    previous = json.loads(args.v1_manifest.read_text('utf-8'))
    if (previous['baseRevision'] != REVISION or previous['baseGgufSha256'] != BASE_SHA
            or sha256(args.base) != BASE_SHA or previous['quantization'] != 'Q4_K_M'
            or previous['contextSize'] != 4096):
        raise ValueError('Existing pinned base integrity mismatch')
    server = v1_root/'bin/llama-server.exe'
    for name, digest in previous['runtimeFileSha256'].items():
        if sha256(server.parent/name) != digest:
            raise ValueError('Verified llama.cpp runtime changed')
    # Offline base config is the same previously verified conversion input.
    config = json.loads((args.base_config/'config.json').read_text('utf-8'))
    if (sha256(args.base_config/'config.json') != BASE_CONFIG_SHA
            or config.get('model_type') != 'qwen3' or config.get('num_hidden_layers') != 36
            or (args.base_config/'model.safetensors.index.json').exists()):
        raise ValueError('Unexpected base conversion configuration')
    output.mkdir(parents=True)
    lora = output/'pilot-v3-lora-f32.gguf'
    command = [sys.executable, str(args.converter/'convert_lora_to_gguf.py'),
               '--base', str(args.base_config), '--outtype', 'f32', '--outfile', str(lora),
               str(args.v3_root/'adapter')]
    env = dict(os.environ, HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1',
               USE_TF='0', USE_FLAX='0', OMP_NUM_THREADS='1')
    with (output/'lora-conversion.log').open('x', encoding='utf-8') as log:
        subprocess.run(command, check=True, env=env, stdout=log, stderr=subprocess.STDOUT)
    sys.path.insert(0,str(args.converter/'gguf-py'))
    count = verify_lora(args.v3_root, lora)
    manifest = dict(previous,
        pilotVersion='V3', modelVersion='Pilot-V3-Qwen3-4B-Instruct-2507-Q4_K_M-LoRA-F32-b11321',
        adapterSha256=V3_ADAPTER_SHA, baseGgufPath=str(args.base.resolve()),
        loraGgufFile=lora.name, loraGgufPath=str(lora), loraGgufSha256=sha256(lora),
        loraGgufBytes=lora.stat().st_size, loraTensorBitExactCount=count,
        serverPath=str(server), evaluatedAdapterRoot=str(args.v3_root.resolve()),
        evaluatedArtifactManifestSha256=V3_ARTIFACT_MANIFEST_SHA,
        evaluatedArtifactFilesVerified=verified_files,
        evaluationMetadataSha256=sha256(args.evaluation_root/'evaluation-metadata.json'),
        adapterConfigRevision=None, baseRevisionProof='Checksum-bound fit and evaluation metadata',
        packagingMethod='Preserved evaluated V3 to bit-exact F32 LoRA; same existing Q4_K_M base',
        qualityStatus='EVALUATION_NO_GO_LOCAL_ACCEPTANCE_ONLY',
        runtimePreflightStatus='NOT_RUN', realE2EStatus='NOT_RUN',
        modelLaunchRequiresUserRamConfirmation=False,
        createdUtc=datetime.now(timezone.utc).isoformat(), conversionCommand=command,
        v1FallbackManifestPath=str(args.v1_manifest.resolve()), v1FallbackManifestSha256=V1_DEPLOYMENT_SHA)
    checksum = write_manifest(output/'court-model-manifest.json',manifest)
    print(json.dumps({'modelVersion':manifest['modelVersion'], 'adapterSha256':V3_ADAPTER_SHA,
        'loraSha256':manifest['loraGgufSha256'], 'bitExactTensors':count,
        'manifestSha256':checksum, 'package':str(output), 'baseSharedReadOnly':True,
        'qualityStatus':manifest['qualityStatus']},indent=2))


if __name__ == '__main__': main()
