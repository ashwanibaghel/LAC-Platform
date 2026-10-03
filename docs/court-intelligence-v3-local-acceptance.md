# Preserved Pilot V3: local acceptance, not production promotion

## Identity and immutable fallback

- Evaluated V3 adapter SHA256: `d57f20ada9a5d07b9f472385393127ba6ae1a8191c7247441cd5473448965175`.
- Fit artifact-manifest SHA256: `2f219f3567c52befbc528943b7352a7dc66a7c9055b27eee442d96b3c29de934`; all 32 files verified.
- Evaluation metadata SHA256: `93c8a1aa14b4e5480de3ab9ee3b58b588f1d2a8c452e44ddc22e0c549ff3a891`.
- Base: `Qwen/Qwen3-4B-Instruct-2507`, immutable revision `cdbee75f17c01a7cc42f958dc650907174af0554`.
- The PEFT adapter's `revision:null` is preserved. Checksum-bound fit and evaluation metadata both establish the same immutable base revision.
- Existing Q4_K_M base SHA256: `9657e9d21175ed290fa4ec3662fffb61a001463ac4ec1abd62deaa509b440710` (shared read-only; no conversion/requantization of base).
- New F32 V3 LoRA GGUF SHA256: `f002c12aef178431cd56bc482350f3733e92f51972a668ed119a7d946e68daf1`; all 288 tensors bit-exact versus the preserved V3 safetensors.
- V3 package: `D:\LAC-CourtAI-V3-20261003-r1`; manifest SHA256 `926a2dc2002941987c40b4ddd656e5e4b12d5c5e2fa74423fb3500b8f026631b`.
- V1 fallback package remains intact at `D:\LAC-CourtAI-V1-20261003`. Its original manifest SHA256 remains `d3fb70af9dd325e4e2a13eb8015ebc52208aadb6a370c103fb17f0798922204c`.
- llama.cpp b11321, CPU, threads 2, parallel 1, ctx 4096, mmap default, no mlock, loopback 8096/8097. Seed 17, temperature 0, prompts/schema/semantic gates unchanged.

The preserved V3 evaluation decision is still **FAIL / NO-GO for release**. No fresh pre-fit blind certificate exists; multi-order/positive-action gains were not established and compliance regressed. This package does not change that decision. User explicitly requested a controlled local real-case acceptance comparison, not IIS deployment or production promotion.

## Reproducible offline packaging

Run `tools/court-order-intelligence/package_pilot_v3.py --help` for the explicit input paths. It verifies the preserved fit and evaluation files, the original V1 manifest, base checksum and runtime files before conversion. It refuses existing output directories or any destination inside V1. Use the previously verified **config-only** `pinned-lora-config` directory, not the weight-index-bearing `pinned-base-config` directory. The latter makes the upstream converter attempt to read absent base shards; the initial failed preparation log is preserved separately at `D:\LAC-CourtAI-V3-20261003\lora-conversion.log`.

No adapter/config/tokenizer is rewritten. Converter runs offline with the same pinned llama.cpp source. No training, download, threshold repair, new base or new quantization is involved. Model weights stay outside Git.

## Product/runtime blockers retained and corrected

1. Explicit refresh can be configured with `--refresh-request-timeout-seconds` (1–300, default 120) and `--refresh-case-timeout-seconds` (1–1800, default 900). Acceptance uses 300/1800. Existing max-one-correction and all-chunks-before-facts rules remain unchanged.
2. Atomic publication keeps same-directory temporary file, flush/fsync and `os.replace`. Windows-only sharing errors 5/32/33 get at most six attempts, total backoff 0.77 seconds. Failure preserves the previous file and removes the temp.
3. API refresh-state reads allow `FileShare.Delete`. Windows polling must not prevent atomic replacement. `current.json` reader already permitted delete sharing.
4. API-to-Python Ask/Refresh JSON is bounded and buffered so it has Content-Length. Streaming `PostAsJsonAsync` produced a null length, while the local Python receiver requires one. Regression test failed for both requests before the transport fix and passed after it. No automatic request replay is added.
5. Incomplete, failed, review-required or source-review-required orders never produce a misleading global “no direct LAC action” conclusion. Premium layout/CSS is unchanged.
6. Generic caption parsing removes a repeated oral-author name from bench display, preserving the verbatim caption evidence.

## Pre-output source oracle: W.P.(C) 2493/2023

Official order: 25 September 2026, uploaded 30 September 2026. Official URL: `https://delhihighcourt.nic.in/app/showFileJudgment/PMS25092026CW24932023_130944.pdf`.

PDF SHA256: `d448cb2dd381aa1771982c5f23c92422e29c4ea283d1a7710c262bbf848a1b73`. Ten native-text pages, 58 anchors, ten original bounded chunks. Registered case ID `dd01a940-4a7d-4729-a3d6-5e20e5809cbb`.

- Pages 1–2 paragraph 2: petition seeks compensation under Section 24(1)(a) of the 2013 Act. Relief sought is not relief granted.
- Earlier quoted 2018 proceedings/directions are historical, not fresh 2026 outstanding directions.
- Page 8 paragraphs 12–13, page 9 quoted affidavit and paragraph 14: LAC's ownership/title/non-payment stand is respondent submission; not a newly established Court finding about ownership.
- Page 9 paragraph 15: Court records no compensation paid to any person as of that date.
- Page 9 paragraph 16: Court records NHAI is stated to have deposited compensation with LAC; retain the source qualifier.
- Pages 9–10 paragraph 17: LAC and then concerned ADJ to consider reference if required, examine documents and process compensation according to law.
- Page 10 paragraph 18: petitioner permitted to file compensation claim.
- Page 10 paragraph 19: LAC **may** release compensation after examining documents. If need arises, LAC to make a Section 30/31 reference. Six months concerns adjudication of that reference; not an unconditional LAC payment/filing deadline.
- Page 10 paragraph 20: petition and pending applications disposed of in those terms. This structured source finding must not silently mutate canonical CourtCase status/NDOH.

Only exact order/page/source passages may support facts or answers. Do not fill missing propositions manually or change guards to fit this oracle. Failure of the unchanged model/gates is an acceptance failure, not permission to weaken them.

## Real registered-case result: REAL E2E NOT ACCEPTED

Run on 3 October 2026 from branch `codex/court-intelligence-v1-office-pilot`, previous HEAD `5acc2d2f7c928228a7972264e0ca68c173531b99`. V3 is the packaging/runtime target; the existing branch name is retained. No main merge or IIS deployment.

The actual authenticated browser flow reached the real API, non-demo runtime and V3 model. This was not a direct-service substitute for the product flow. API/bootstrap/seed/background workers/DHC auto-sync were disabled; the same existing DB and absolute extraction root were used.

- Refresh began `2026-10-03T11:23:00.192600Z` and reached terminal `CompletedWithReview` at `11:41:37.348371Z`: **1117.16 seconds (18m37s)**.
- The exact official PDF was downloaded, its known SHA/date/caption confirmed, and all ten pages yielded native text. First model call began 2.742 seconds after refresh start; that combined interval includes source fetch/preparation, not an independently timed PDF substage. Earlier independent PDF audit download took 2.219 seconds.
- Six original chunks returned HTTP 200 and progressed through schema/evidence expansion. No correction or request replay was used. Seventh request timed out at the unchanged configured acceptance bound of 300 seconds, without a complete response. Model log records cancellation/release of that task. Remaining three chunks were **not attempted**.
- Persisted order: `NeedsReview`, `Local extraction unavailable: ReadTimeout`, `allSelectedChunksProcessed=false`, **zero verified facts and zero actions**. All partial chunks were withheld. This is safety working, not successful case intelligence.
- Persisted refresh is terminal, the button re-enables, and passive browser reload shows the same case-scoped artifact. No WinError 5 occurred. Correct two-judge bench and page-1 evidence are visible.
- UI explicitly states the office-action check is incomplete; it does not infer absence of a LAC obligation. Current position remains unconfirmed; latest order cannot be summarized. Supported final disposition from the source oracle was not extracted/displayed.

| Original chunk | Model request seconds | Outcome |
|---|---:|---|
| 1 | 170.55 | HTTP 200; validated chunk, not publishable alone |
| 2 | 99.17 | HTTP 200; validated chunk, not publishable alone |
| 3 | 122.06 | HTTP 200; validated chunk, not publishable alone |
| 4 | 119.70 | HTTP 200; validated chunk, not publishable alone |
| 5 | 129.52 | HTTP 200; validated chunk, not publishable alone |
| 6 | 167.36 | HTTP 200; validated chunk, not publishable alone |
| 7 | 300.03 | ReadTimeout; no completed response |
| 8-10 | Not attempted | No fabricated duration/result |

The original V1 exact-chunk probe returned valid output in 101.44 seconds with 1966 of 1967 prompt tokens cached. That does **not** prove the original V1 failure was solely its 120-second timeout. V3's first real cold response took 170.55 seconds and its third took 122.06 seconds, establishing that 120 seconds is insufficient for those measured V3 requests. Increasing to the bounded 300/1800 pilot setting did **not** solve this full real-order run. No timeout was increased again.

### Source comparison and unmet acceptance

| Output / missing output | Expected source truth | Order/page | Guard and UI result |
|---|---|---|---|
| No substantive brief, current position, active actions or final outcome | Compensation-related reasoning, conditional LAC/ADJ processing and final disposal exist | 25 Sep 2026, pages 9-10 | Whole-order coverage guard withheld partial facts; UI disclosed incompleteness. Acceptance FAIL |
| No verified petitioner-relief or LAC-stand propositions | Relief sought and attributed LAC submissions exist, not automatic Court findings | 25 Sep 2026, pages 1-2 and 8-9 | No unsupported substitute created; product completeness FAIL |
| No promoted 2018 action/compliance/next hearing | Historical quoted directions cannot become current; six months is not an unconditional LAC deadline | 25 Sep 2026, pages 2-3 and 10 | No facts emitted. Safety withheld, but full attribution/action quality remains untested |
| Bench now contains only the two CORAM judges | Oral-author repetition is not a third judge | 25 Sep 2026, page 1 | Generic parsing fix and rendered evidence PASS |
| Terminal review-required state, not a stuck processing state | Processing ended with a review item | Runtime state | Atomic publication and passive reload PASS |

No unsupported Court finding, invented LAC action or false compliance reached the UI; there were zero verified substantive propositions. The six intermediate responses are diagnostic material only, not a certificate of whole-order truth/quality.

The eight requested grounded questions were **NOT RUN**, because this same case did not pass and has no verified facts:

1. Is case ka poora scene batao.
2. Petitioner kya maang raha hai?
3. LAC ka stand kya hai?
4. Court ne kya find/direct kiya?
5. Last 3 orders me kya hua?
6. Kaunsi direction abhi active hai?
7. Agli hearing se pehle LAC ko kya karna hai?
8. Next hearing kab hai?

Their model latency, evidence/attribution accuracy and production readiness are not claimed. Second-case processing and A-B-A isolation acceptance were **NOT RUN**, per the requirement to pass case A first. The durable register contains only one exact dated order observation for each of these two cases; no three-order history was invented. Automated case-isolation tests passed, which is distinct from a real second-case acceptance.

### Memory and runtime state

During the real refresh: minimum Available **0.00061 GiB (about 0.62 MiB)**; peak commit **16.5495 GiB**; minimum commit headroom **0.2634 GiB**; peak sampled model working set **3.0764 GiB**, OS-recorded lifetime peak **3.2281 GiB**; peak private memory **4.0175 GiB**. Immediately before refresh Available was **0.6898 GiB**, commit **15.298 GiB**, headroom **1.372 GiB**. Early load sample Available was **2.181 GiB**, commit **12.121 GiB**, headroom **2.533 GiB**. V3 model readiness took **18.209 seconds**.

Paging became sustained/severe on chunk 7: median approximately **32,117 page-ins/sec** over 299 samples, maximum across the refresh approximately **101,529/sec**. Browser observations completed in roughly 0.2-1.5 seconds and diagnostic shells returned; no observed OS hang, allocation failure or model crash. This is not an independent user responsiveness certification. Paging and the timeout coincide; memory alone is not proven the sole causal explanation. Windows resized its already-configured pagefile automatically; no pagefile setting was changed.

After timeout/cancellation and terminal artifact publication, only idle V3 llama-server PID 16800 was stopped to release sustained pressure. No altered configuration or second attempt. End sample after stopping: Available **2.695 GiB**, commit headroom **3.380 GiB**. API 5088, frontend 5175 and non-demo runtime 8097 remain loopback-only and running; 8096 is stopped. No unrelated apps were killed.

Read-only DB verification still shows case A `Pending`, Revision 2, no DisposedDate and proceeding NextDate `2026-09-25`; one existing order observation. Case B remains `Pending`, Revision 2, proceeding NextDate `2026-09-28`; one order observation. No canonical status/NDOH/compliance mutation, migration, seed, DHC sync/search/CAPTCHA/review decision, cloud inference or production deployment occurred. The one exact known official PDF fetch was explicitly part of this acceptance; do not call that zero DHC HTTP traffic.

### Tests and preserved evidence

- Python Court runtime suite: **161/161 PASS**, 14.237 seconds, including V3 identity checks and real Windows sharing-lock tests.
- Focused .NET Court Intelligence tests: **10/10 PASS**, 28 seconds on final rerun; transport tests also compiled/passed after the fix.
- All frontend `*.test.mjs`: **97/97 PASS**, 4.362 seconds final rerun.
- Direct Vite production build: **PASS**, 173 modules, 0.870 seconds; existing large-chunk warning remains.
- Read-only TypeScript HEAD-versus-current comparison: **39 baseline / 39 current, zero new diagnostics**. This is not a clean full `tsc` result.
- `git diff --check`: PASS. Full .NET suite not rerun; focused scope was requested for this fix.
- An over-broad `*.mjs` test invocation also selected the manual `award-review-visual.mjs` harness; it failed because its required port 5173 is not running. The proper complete `*.test.mjs` unit suite was rerun and passed. No Award source or service was changed to satisfy that harness.

All new evidence is under `D:\LAC-CourtAI-V3-20261003-r1\acceptance`: `provider-calls.jsonl`, `memory.jsonl`, `model.stderr.log`, `api-r1.stdout.log`, `v3-terminal-current.json`, `v3-terminal-refresh.json`, `post-v3-canonical-state.json`, `typescript-comparison.json`, `real-case-outcome.png` and `real-case-terminal.png`. The earlier V1 artifact/state were captured separately before refresh. V1 package, adapter, logs/cache/partials and manifest remain intact, with hashes reverified after V3 acceptance. New model weights/artifacts are outside Git.

The verified V3 deployment manifest remains immutable; this report supplies the actual failed acceptance result rather than rewriting package identity receipts. Existing V3 evaluated release **NO-GO** remains unchanged. No retraining, threshold/prompt/schema/safety-gate change, requantization, architecture change or automatic fallback was made.

## Exact changed files

Includes the previously completed, uncommitted V1 packaging/recovery helpers, preserved as A/B fallback infrastructure rather than discarding that work.

```text
docs/court-intelligence-v1-pilot-packaging.md
docs/court-intelligence-v3-local-acceptance.md
docs/court-intelligence-v3-package-receipt.json
scripts/start-court-local-model.ps1
src/LAC.Api/CourtIntelligenceCaseData.cs
src/LAC.Api/CourtIntelligenceQuestions.cs
src/LAC.Web/src/court/CourtIntelligence.tsx
src/LAC.Web/tests/court-intelligence-integration.test.mjs
src/LAC.Web/tests/court-intelligence.test.mjs
tests/LAC.Tests/CourtIntelligenceIntegrationTests.cs
tools/court-order-intelligence/chronology.py
tools/court-order-intelligence/finalize_pilot_v1.py
tools/court-order-intelligence/package_pilot_v1.py
tools/court-order-intelligence/package_pilot_v3.py
tools/court-order-intelligence/real_case.py
tools/court-order-intelligence/recover_pilot_v1.py
tools/court-order-intelligence/serve_questions.py
tools/court-order-intelligence/test_chronology.py
tools/court-order-intelligence/test_model_startup.py
tools/court-order-intelligence/test_pilot_manifest.py
tools/court-order-intelligence/test_pilot_packaging.py
tools/court-order-intelligence/test_pilot_recovery.py
tools/court-order-intelligence/test_pilot_v3_packaging.py
tools/court-order-intelligence/test_real_case.py
tools/court-order-intelligence/test_runtime_publication.py
tools/court-order-intelligence/worker.py
```
