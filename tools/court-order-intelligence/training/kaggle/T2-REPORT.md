# T2 actual Kaggle smoke — 2 October 2026

**Plumbing PASS with a disclosed FP16 skipped-update caveat.** This is not model
quality, validation accuracy, blind performance, production readiness or legal
answer correctness. The successful parser proof uses schema-constrained decoding.
Unconstrained generation is **not** proven reliable.

Branch: `codex/court-intelligence-training-v1`.
Previous infrastructure SHA: `d757b232e3a97ed8822974da33c0e87e77e08dd3`.
The report commit is the commit containing this document; its SHA is supplied in
the final handoff. Main was neither checked out nor merged.

## Actual execution

Model: `Qwen/Qwen3-4B-Instruct-2507`.
Immutable revision: `cdbee75f17c01a7cc42f958dc650907174af0554`, verified on Kaggle.
Existing authenticated CLI used; credential files/values were not inspected,
printed, copied or committed. Dataset and both kernels were created private.

GPU startup preflight: Tesla T4, 15,636,037,632 bytes VRAM (14.56 GiB),
torch 2.10.0+cu128, CUDA 12.8. Torch's default BF16 probe included emulation.
The actual pinned training process verified native BF16 **false** and used FP16.
One GPU was exposed to training, including on dual-T4 hosts.

Actual training packages: torch 2.6.0 / CUDA 12.4, transformers 4.56.2,
peft 0.17.1, bitsandbytes 0.47.0, accelerate 1.10.1, datasets 4.1.1,
jsonschema 4.25.1. Final inference-only proof additionally installed
lm-format-enforcer 0.11.3. Its installation is recorded in the downloaded log.

Data: original 22 public development examples, explicitly SMOKE_ONLY,
NOT_EVALUATION, NOT_BLIND, NOT_QUALITY_EVIDENCE. All 22 retained; zero truncated
or oversize-skipped examples. Private workbook and office data were not uploaded.
Permanent development/train/validation/blind bytes and semantics stayed unchanged.

QLoRA: 4-bit NF4, double quantization, Q/K/V/O projections, rank 8, alpha 16,
dropout 0.05, 5,898,240 trainable parameters. Batch 1, accumulation 4,
gradient checkpointing, learning rate 0.0001, seed 20261002, context cap 2048.
The bounded forward/backward memory probe passed before training; no OOM.
Sampling cycles through the original examples without adding dataset records.

Training: **6 Trainer global steps, 5 applied optimizer updates**, 56.978 seconds.
Step 2 logged a non-finite FP16 gradient and the scaler skipped that update.
Downloaded checkpoint-6 optimizer state was read with `weights_only=True`:
all 288 parameter states have step=5. Do not call this six successful updates.
The caveat is recorded rather than extending training beyond the six-step cap.
Peak allocated GPU memory: 7,399,721,984 bytes (6.89 GiB).

Checkpoint-2 retained adapter, optimizer, scheduler, RNG, scaler and Trainer state.
A new Trainer actually loaded it and reached global step 6. The six-step schedule
was preserved across the deliberate checkpoint pause. This proves same-session
checkpoint restore; a separate quota-expiry/session restart was not simulated.
Cross-session exact-bundle/config instructions remain in README.md.

Adapter saved, original model released, exact base reloaded and adapter reloaded.
Separate inference-only proofs reloaded that **unchanged** adapter; zero additional
training. Adapter SHA256:
`1ba2f5651e348a85bbc0caa6164657fa6f83c1c43d1018acad5f163c013d1970`.

## Representative inference / strict parser

Final schema-constrained generation produced:

| Task | Structural output summary | Frozen parser |
| --- | --- | --- |
| Attribution | anchors 0/1: LAC_OR_RESPONDENT_SUBMISSION; fields filing/observation; Current | Accepted |
| Office action | claims [] | Accepted |
| Date-specific/timeline | fact IDs 0–6 | Accepted |
| Digest | fact IDs 0–2 | Accepted |
| Compliance | claims [] | Accepted |

All five raw outputs and parsed JSON are retained in
`reports/t2-constrained-smoke-evaluation.json`. They passed JSON/schema,
known retrieved-ID checks and frozen anchor expansion. No gold-target
substitution, JSON-from-prose extraction, fabricated citation or output repair.
Schema token filtering restricts structure, **not factual correctness**.

Initial unconstrained inference accepted 4/5: attribution invented an `anchors`
shape and was rejected. Supplying the exact schema in the prompt accepted 4/5:
compliance returned integer claims rather than objects and was rejected.
Both evaluations are preserved. The final proof enforces the unchanged T1 schema
at generation time, consistent with the frozen provider's schema response format.
No production provider or parser was changed.

## Recoverable artifacts / failures / quota

Actual adapter, tokenizer, checkpoints, logs, config, manifest and metadata:
`C:/Users/ashwa/.codex/worktrees/court-intelligence-training-v1/LAC-Platform/tools/court-order-intelligence/training/local-private/t2-attempt4/lac-t2-smoke/`

All 42 checksummed training artifacts downloaded and verified. The final proof's
evaluation checksum and adapter SHA also verified. These local directories are
Git-ignored; Git contains only code/config/small reports/checksums, no weights.
Original frozen upload bundle is retained under `local-private/t2-run-bundle-v3`.

Actual retries: relative-path dataset upload failed locally (absolute path worked);
kernel v1 failed before training because Kaggle expanded the ZIP; v2/v3 resumed
only to step 5. Inspection of the pinned Trainer revealed the trailing partial
accumulation after mid-epoch skip; deterministic cyclic sampling fixed it.
An interrupted archive download was replaced by verified per-file downloads.
Windows CLI Unicode log output was retried with PYTHONUTF8=1. No OOM occurred.

Training kernel: `ashwanibaghel9027/lac-t2-qlora-smoke`, successful execution v4.
Final inference-only kernel: `ashwanibaghel9027/lac-t2-contract-proof`, v2.
Dataset: `ashwanibaghel9027/lac-court-t2-smoke-public-seed-v1`, v3.
Both kernels are now complete; no long-running training remains.

Final training process elapsed: 170.525 seconds; final constrained proof:
120.088 seconds. Sum of six completed training/proof kernel log spans is about
1,965.3 seconds (32.8 minutes), excluding preflight. This is **not account-billed
GPU quota**: queue/startup/output upload and assigned-device accounting differ.
Exact account quota consumption was unavailable; no fabricated quota figure.

## Verification and scope

13 T2 tests, 26 foundation tests and 116 existing Court Intelligence tests passed
(155 total). The real saved outputs are independently revalidated by a local test.
`git diff --check` passed. No .NET/frontend rerun was needed: no product source,
API/domain/schema/migration/UI change. No live DHC request was made.

No quality or blind claim. No credentials or model weights committed. No private
workbook upload. No protected split change. No main merge. No office DB/storage
change or deployment. No GGUF, Pilot V1 or T3 started. STOP after report push.
