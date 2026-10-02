# T4-A Pilot V1 run plan

One serious run, pinned Qwen3-4B-Instruct-2507 revision
`cdbee75f17c01a7cc42f958dc650907174af0554`.

Gate: 194 gold examples; 113 train, 44 validation, 37 blind. Separate
15 matter groups (11/2/2). 22 native-source orders; 33 excluded annotations.
T1 regression set remains unchanged. 33 foundation/pilot, 17 Kaggle tooling
and 116 existing Court Intelligence safety tests passed before launch.

Single GPU, rank 8 / alpha 16, Q/K/V/O, NF4 double quantization,
context 2048, batch 1 / accumulation 4, checkpointing, 80 global steps,
learning rate 5e-5, default Trainer clipping 1.0, native BF16 if supported
otherwise FP16 on T4. Existing T2 6.89 GiB probe motivates unchanged memory
dimensions; LR is halved for stability. Bounded probe fallback may reduce
context but must report all oversized IDs and retain all eight tasks.

80 steps means 320 microbatches, approximately 2.83 passes over 113 original
train examples before context exclusions; no new examples or packing.
Same deterministic cyclic stream and schedule across deliberate save/resume
at step 10. Save every 20 steps, retain latest three complete checkpoints.
Final optimizer-state counters prove applied vs scaler-skipped updates.
More than four skipped updates fails the Pilot training stability gate.

Private Kaggle kernel/dataset, public minimized evidence only. No workbook,
whole native pages, credentials, downloaded PDFs or base weights in upload.
Two-hour kernel execution bound; no parameter sweep. At most one justified
corrective technical-failure run, not a blind-result-driven training rerun.

Evaluation: frozen 81 held-out examples, deterministic constrained generation
and identical frozen runtime parser. Stock is fresh pinned NF4 base with
adapter disabled; fine-tuned is that exact base with adapter enabled.
Gold targets never enter generation. Persist actual raw outputs for manual
full-source-chain review. JSON/exact-ID metrics alone cannot certify officer
usefulness or deployment; deployment gate stays unapproved pending review.
