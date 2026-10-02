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
