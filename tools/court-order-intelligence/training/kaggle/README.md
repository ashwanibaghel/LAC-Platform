# T2 Kaggle smoke (not quality training)

Accepted T1: `78326829f7a5310970908b4a91e5e5df08e35593`.
Exact model: `Qwen/Qwen3-4B-Instruct-2507` at
`cdbee75f17c01a7cc42f958dc650907174af0554`.

T2 is **NOT PASSED until actual Kaggle GPU artifacts prove every gate**.
CPU serialization/parser tests do not prove GPU loading, QLoRA, resume or reload.
No loss, parser acceptance or seen-example output is a quality/accuracy/blind metric.
Permanent development/train/validation/blind files remain unchanged.

## Prepare locally

```powershell
python tools/court-order-intelligence/training/kaggle/prepare_smoke_dataset.py --output tools/court-order-intelligence/training/local-private/t2-smoke-bundle
```

This refuses a nonempty destination. It verifies accepted T1 manifest/annotation
hashes and every source-reviewed example, then builds an explicit 22-example
SMOKE_ONLY / NOT_EVALUATION / NOT_BLIND / NOT_QUALITY_EVIDENCE copy.
Only allowlisted public seed, exact parser source and training code/config go
into the bundle. **Never upload the parent repository/directory or workbook.**

Create a **PRIVATE** Kaggle dataset containing only this bundle and a **PRIVATE**
notebook. Verify both privacy settings in Kaggle before execution. API credentials
are not required inside the notebook; the base model is public. Do not paste
tokens/passwords in chat, notebooks or repo. Account login and GPU entitlement
must be established by the user; this repo cannot create credentials.

## Run on Kaggle, not on the office PC

Attach the private bundle dataset. Set GPU accelerator dynamically; do not
assume a particular card. Enable Internet for the public pinned model download.
Install requirements, then **restart the Python kernel** before importing torch.
Paths below are examples; inspect your dataset's actual mount name.

```python
!python -m pip install -r /kaggle/input/lac-t2-private/requirements.txt
# Restart kernel using Kaggle UI now, then run the next command.
!python /kaggle/input/lac-t2-private/train_smoke.py --bundle /kaggle/input/lac-t2-private --output /kaggle/working/lac-t2-smoke --ack-private-notebook
```

Pinned torch/torchvision/torchaudio prevent optional incompatible preinstalled
imports. The first actual GPU probe remains the compatibility proof; local
dependency resolution is not a tested Kaggle environment. No TRL is needed.
Single GPU device 0 is used, while name/VRAM/CUDA/bf16/versions are recorded.
Hardware-dependent bf16/FP16 is selected without guessing a GPU model.
The CLI launcher `run_kaggle.py` handles both ZIP and Kaggle-expanded mounts,
copies only manifest-listed files, installs the pins, then starts a fresh Python
subprocess with `CUDA_VISIBLE_DEVICES=0`. This prevents Trainer from silently
doubling batch size on a dual-T4 host. BF16 requires native support, not emulation.

Settings: 4-bit NF4/double quant, rank 8/alpha 16 Q/K/V/O LoRA, checkpointing,
batch 1, accumulation 4, maximum 6 optimizer steps, sequence cap 2048.
Forward/backward probe runs first. Only probe OOM permits bounded 1024 then 512
context retries, with no evidence/JSON truncation. Oversize records are explicitly
reported, and every representative task must survive. Training/load OOM fails
with recoverable metadata rather than automatically consuming quota repeatedly.
Stop and inspect before a manually approved adjusted run.

Training masks all system/user/prompt tokens; loss covers assistant JSON only.
Token-prefix checking protects the official Qwen chat boundary. No synthetic
legal essays, data inflation, cloud legal Q&A, DHC calls or office endpoints.

## Checkpoint / resume

The default run uses the six-step schedule, deliberately pauses at step 2,
saves optimizer/scheduler/RNG/adapter, creates a
new Trainer, reloads that checkpoint and continues to global step 6. The actual
resume step/result is written to metadata. Until a real run exists this is a
procedure, **not a proven resume result**.
Sampling cycles deterministically through the unchanged original examples. With
the pinned Trainer, finite mid-epoch resume can lose the final partial gradient
accumulation. An iterable avoids that boundary, gives four microbatches per
update and stops at six steps; it adds no records to the dataset.

Keep `checkpoints/`, dataset manifest and training-config.json together. To
continue in a new Kaggle session, upload the previous **trusted** artifact folder
as another PRIVATE dataset (never accept arbitrary pickle checkpoints), then:

```python
!python /kaggle/input/lac-t2-private/train_smoke.py --bundle /kaggle/input/lac-t2-private --output /kaggle/working/lac-t2-resumed --resume /kaggle/input/lac-t2-checkpoint/checkpoints/checkpoint-2 --ack-private-notebook
```

Dataset/config must match exactly. Choose a fresh output directory; prior
artifacts are never overwritten. A step-6 completed checkpoint is not resumed
past the bounded smoke cap. Do not resume as a long Pilot V1 run.

## Artifacts and acceptance

Output contains adapter/tokenizer, recoverable checkpoints, training-log.jsonl,
training-config.json, run-metadata.json, dataset-manifest.json,
smoke-evaluation.json and artifact-checksums.json. A sibling `*-artifacts.zip`
includes only these, not base-model cache. Download using Kaggle's output UI
or authenticated CLI into local ignored storage. Record the downloaded local
adapter location/checksums in the report. No adapter/checkpoint weights in Git.

The adapter is saved separately, original GPU model freed, exact pinned base
loaded again, adapter reloaded, then five different task types generated.
Raw outputs go through JSON/schema and frozen anchor expansion / retrieved-ID
validation. No JSON-from-prose repair, guessed citation, target substitution or
automatic inference retry. Every representative task must pass for T2 PASS.
Expected gold is never used to fake a generated output. Model quality is not
required; nevertheless parser failures truthfully mean T2 is incomplete.

HF generation must receive the exact target JSON schema in its system message,
because unlike the frozen production provider it has no JSON-grammar parameter.
`inference_messages` supplies only that schema, not any expected answer. A
separately reported `reload_smoke.py` proof may mount trusted previous private
kernel output, verify adapter checksums, reload the unchanged adapter and run
the five tasks with this explicit contract. It performs **zero training steps**.
Use pinned `lm-format-enforcer==0.11.3` prefix-token filtering for the unchanged
T1 schema, matching the frozen provider's schema-constrained response format.
This is generation-time structural restriction, not output repair, factual
verification or proof that unconstrained generation obeys the contract.
Keep initial rejected outputs in the report; never repair them or replace them
with gold. FP16 scaler-skipped updates must be distinguished from Trainer global
steps; six global steps alone do not prove six applied optimizer updates.

Actual Kaggle account quota consumption must be read from the account UI; script
GPU-process elapsed time is separately labelled and is not billed/quota time.
No GGUF, no base-model merge, no office deployment, no T3/Pilot V1.

References: [pinned Qwen files](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507/tree/cdbee75f17c01a7cc42f958dc650907174af0554),
[Transformers 4-bit](https://huggingface.co/docs/transformers/v4.56.2/quantization/bitsandbytes),
[PEFT quantized training](https://huggingface.co/docs/peft/main/developer_guides/quantization).
Schema-decoding integration: [LM Format Enforcer 0.11.3](https://github.com/noamgat/lm-format-enforcer/tree/v0.11.3).
