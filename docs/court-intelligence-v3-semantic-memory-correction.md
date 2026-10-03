# V3 semantic conflict and memory correction - 3 October 2026

## Result

Cached required source oracle: **PASS**. Real registered-case E2E: **NOT ACCEPTED**
(not executed: the runtime-switch command was rejected by tool policy before it
executed). Do not interpret offline processing as real browser acceptance.

Starting HEAD: `900eef1460e7880cec63a52de9e990b5cba90de8`, branch
`codex/court-intelligence-v1-office-pilot`. Work is preserved, uncommitted.
No main merge, IIS deployment, model/LoRA/base replacement, retraining, schema,
migration, canonical case status/NDOH change, CAPTCHA, assisted resume or DHC sync.

## Generic semantic correction

`proposition_kind` distinguishes descriptive state, mandatory direction,
conditional direction, permission, submission and recorded compliance without
changing model facts, their labels, source evidence or the extraction contract.

Conflict detection compares literal performed-state subject/predicate, scope,
explicit time/identifiers, actor and attribution. It does not equate an entire
topic field with one truth value. Explicit Court findings can conflict with the
same recorded state even when their model field is `finding`. Unknown subjects
or multi-predicate prose are not guessed. Opposed submissions remain review work
without being promoted to Court facts. Genuine opposed state assertions remain
withheld. Conditional/permission expressions cannot generate mandatory actions.

Cached 25 September 2026 order, W.P.(C) 2493/2023, replayed without a model call:

- Petitioner Section 24(1)(a) prayer retained as petitioner submission, page 1.
- 2018 eight-week quoted direction remains Quoted/historical, pages 2-3.
- LAC title/nonpayment stand remains respondent submission, pages 8-9.
- Current nonpayment fact remains available, page 9.
- NHAI deposit retains "is stated to have" qualification, page 9.
- Permission to file a claim retained, page 10.
- Three exact-source conditional entries retained: paragraph 17 reference/documents
  (pages 9-10), paragraph 19 permission to release after examination (page 10),
  and contingent Section 30/31 reference (page 10).
- Six months remains verbatim reference-adjudication evidence, never a payment or
  reference-filing due date. Conditional entries have no inferred completion.
- Petition disposal retained, page 10.
- Mandatory actions, before-next-hearing actions and invented payment deadlines: 0.

Adjacent evidence fragments are carried inside the conditional entry's source
for the existing UI evidence component. Generic completeness recovery remains
one additional model call maximum; its review flag cannot be lost. The source
remains NeedsReview for 13 unrepresented nonquoted anchors. This is not a claim
that the complete document or every possible fact has been certified.

## Sequential memory benchmarks

Exact evaluated V3 LoRA/base/server manifest verified by each benchmark. All use
Q4_K_M, V3 F32 LoRA, CPU, parallel 1, mmap default, mlock off, f16 KV, repacking,
cache-ram 0, seed 17 and temperature 0. No prompt or schema changes. Exact build
help confirms batch default 2048, ubatch default 512. No ctx2048, q8/q4 KV retry.

| Configuration | Seconds / calls | Peak private MiB | Peak working set MiB | Min available MiB | Min commit headroom MiB |
|---|---:|---:|---:|---:|---:|
| 3072 / 8 / default batches (prior baseline) | 308.59 / 5 | 2411.2 | 3148.9 | 12.1 | 278.1 |
| 2560 / 8 / default batches | 326.22 / 5 | 2338.6 | 3125.2 | 258.8 | 731.4 |
| 3072 / 8 / ubatch128 | 339.72 / 5 | 2345.2 | 3488.4 | 385.4 | 788.7 |
| 3072 / 8 / ubatch128 / batch512 | 332.56 / 5 | 2323.8 | 3065.1 | 277.9 | 401.2 |
| 3072 / 6 / ubatch128 / batch2048 | 306.81 / 5 | 2332.6 | 3558.5 | 417.0 | 1017.8 |

Each transition changes one variable relative to its stated comparator: ctx vs
baseline; ubatch vs baseline; batch512 vs ubatch128; threads6 vs ubatch128.
Machine: 4 physical/8 logical cores. ctx2560 exact normal prompt tokens are
2143/1830/1834/1148; observed completions fit, with truncated=0. Full raw facts
equal baseline for ctx2560, ubatch128 and batch512. Threads6 has one difference:
the LAC affidavit introduction is Current instead of Historical; substantive
contents remain attributed/quoted and all required oracle propositions persist.
This is semantic oracle equivalence, NOT byte-identical output equivalence.

Selected measured candidate for further acceptance: ctx3072, threads6,
batch2048, ubatch128, f16 KV, cache-ram0. Its per-call latencies are
108.70/68.91/89.81/19.31/15.70 seconds. Four normal calls plus one prayer recovery.
Peak system commit 16.525 GiB; post-load paging median 163.7 pages-input/sec,
peak 112390.5 (bursts remain). Tool/Windows access remained operational; formal
UI lag was not measured. About 78.6 MiB private-memory reduction vs baseline.

Windows dynamically changed the pagefile/commit limit during these runs. No
pagefile setting or unrelated application was altered. Available/commit gains
are observed headroom, NOT a causal claim that batch/thread settings created
all that headroom. Working-set high water is not monotonically reduced. This
candidate is not yet validated as an office production configuration.

## Verification

- Pinned conversion/trainer Python, with its pinned llama.cpp `gguf-py` and
  existing conversion-deps on process PYTHONPATH: **189 tests PASS**, no skips.
  Earlier missing-gguf import was environment setup, not a product regression.
- Fresh focused .NET build/tests, API stopped to release DLLs: **10 PASS**.
- Frontend: **99 PASS**. Direct Vite build: **PASS**, existing chunk warning.
- TypeScript: **39 baseline and 39 current diagnostics; normalized delta 0**,
  verified against a fresh isolated HEAD archive. Baseline debt not fixed here.
- `git diff --check`: PASS.

## Runtime / remaining work

API restarted with bootstrap=false, workers=false, DHC auto-sync=false and same
absolute storage roots; health Healthy/database Reachable. API PID11528,
frontend5175 PID20756. Old question service8097 PID12660 remains loaded with old
code, intentionally not claimed current. No server listens on8096; benchmark
children all stopped. New external launch/log helpers are preserved but were
not started after the runtime-switch command rejection.

Actual refresh/passive reload, eight officer Q&A answers and B/A-B-A isolation
remain **NOT RUN**. The browser still shows the previous unverified artifact;
it was inspected passively, not overwritten with benchmark output. Do not click
refresh until model8096 and freshly loaded non-demo8097 are available.

External receipts: `D:\LAC-CourtAI-V3-20261003-r1\optimization\` in
`correctness-pass-r1`, `memory-ctx2560-r1`, `memory-ubatch128-r1`,
`memory-batch512-r1`, `memory-threads6-r1`, `ts-baseline-r1`, `semantic-e2e-r1`.
No model weights, generated case artifacts, credentials or private workbook
are added to the repository.
