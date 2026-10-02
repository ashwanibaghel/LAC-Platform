# V3 experimental launch priority — 3 October 2026

The user has now explicitly requested starting training with the currently
verified pool rather than continuing collection to the earlier coverage gates.
This is an experimental pilot authorization, NOT a claim that those acceptance
gates passed. Truth/source/actor/attribution/scope/negation/lifecycle checks,
quarantine and frozen V1/V2 protections remain unchanged.

Current verified candidates: 244 / 26 matters / 22 conservative leakage groups.
Attribution: 30 / 13 groups. Positive LAC action: 12 / 4 groups.
Positive compliance: 5 / 2 groups. Certified strong current position: 4 / 4
groups; 15 multi-order-labelled rows must not be represented as 15 full chains.
The final-quality release remains NO-GO. Fresh holdouts are not yet certified
or frozen; do not silently use prior V1/V2 holdouts for training.

Legacy compatibility: 31 of the 32 original quarantine examples recover;
`6108-position` remains quarantined. Original target/passages and the 18 frozen
V1/V2 dataset/review files are unchanged.

Hardware preflight v2 (source 9383ee4): Tesla T4, 15,636,037,632 VRAM bytes,
CUDA 12.4, Torch 2.6.0, BF16 false. 3072 forward/backward passed but only
1,775,042,560 free bytes and not all evidence fits; 4096 OOM. Required context
was 3707 including the inference reserve. This is a failed hardware probe,
not a model-quality result or completed training run.

The V3-only SFT implementation now uses the pinned Qwen3 tensor
`logits_to_keep` after the complete decoder input forward. It omits only LM-head
logits for masked labels, not input tokens, evidence or target tokens. Exact
float32 causal CE loss and all parameter gradients match ordinary Qwen3 on
three tests (contiguous labels, gapped labels, accumulation denominator).
The same proof is repeated on the actual pinned Kaggle stack before a probe.
Architecture, revision, NF4, LoRA rank/modules and SDPA remain unchanged.
Reference: https://github.com/huggingface/transformers/blob/v4.56.2/src/transformers/models/qwen3/modeling_qwen3.py

Current verification: full .NET 762 passed (3.6579 minutes); frontend 87 passed;
direct Vite build passed; runtime Python 116, training Python 137, Kaggle Python
42 passed. Pinned HF actual CPU Trainer resume proof rerun: 320 exact consumed
microbatches, one simulated skip, no double skip. Real CUDA proof pending.

No model weights, credentials, private workbook or raw PDFs are committed.
No office production runtime, backend/domain/schema/migration or main merge
changes. Do not describe experimental training as accepted production quality.

Masked-SFT hardware preflight v3 (08d33bf) encoded all 244 candidates; exact
loss/gradient proof passed with max loss error 0. 3072 forward/backward has
4,660,723,712 free bytes but does not fit every candidate/reserve. Artificially
padding the full record to 4096 now runs without OOM, but only 365,756,416
bytes remain, so it correctly fails the unchanged 2 GiB headroom gate. No
full fit was started. The next probe tests the exact required 3707-token
complete-context cap, not shortened evidence, before optional 4096 padding.

The new experimental fit entry point requires a successful matching hardware
artifact and identical checksummed TRAIN/runtime files. It loads a fresh base
and derives 183 logical steps / 732 weighted microbatches from 244 original
examples and three equivalent passes; it is not another six-step smoke.
Actual Trainer checkpoint at 10 is resumed with exact consumed-stream checking.
There is no forced gradient overflow in production fit. Applied optimizer
updates/scaler skips and actual weighted exposure are reported, not assumed.
Adapter save/reload and eight task-representative inference/parser checks follow
the fit. These TRAIN integration checks are explicitly not fresh-blind accuracy.
