# V3 CPU runtime optimization — 3 October 2026

## Verdict

**REAL E2E NOT ACCEPTED.** Performance improved, but the full source oracle did
not pass. No registered-case refresh, Q&A acceptance or second-case isolation was
attempted after the offline oracle failed. No model/LoRA/prompt/semantic gate,
threshold, canonical Court data, schema, migration, DHC sync or IIS change.

Branch: `codex/court-intelligence-v1-office-pilot`; starting HEAD:
`900eef1460e7880cec63a52de9e990b5cba90de8`. Changes remain uncommitted.

## Authorized source-boundary repair and candidate selection

- Native PDF geometry certifies unique consecutive outer numbered paragraphs.
  Only certified returns reset a leaked quotation/speaker frame. Indented,
  duplicate/out-of-sequence or ambiguous numbering fails closed.
- Explicit LAC affidavit/position attribution takes precedence over a petitioner
  mentioned inside that submission. No assertion is promoted to a Court finding.
- Existing quotation/actor/page/negation/scope/lifecycle validators unchanged.
- Generic lexical candidate selection preserves exact anchors and native adjacent
  context. It does not manufacture facts. It retains current operative candidates,
  final-page findings/disposition, party positions, the petition's relief and a
  bounded historical contrast. Critical overflow is not silently discarded.
- Real cached source: 58 anchors / 10 original chunks → 20 candidates / 4 chunks.
  Omitted 38 anchors remain disclosed; no full-document-completeness claim.
  Existing unrepresented-nonquoted-anchor review rule remains unchanged.

## Immutable identity

V3 model version: `Pilot-V3-Qwen3-4B-Instruct-2507-Q4_K_M-LoRA-F32-b11321`.
Same pinned Qwen3 base revision `cdbee75f17c01a7cc42f958dc650907174af0554`.

| Artifact | SHA-256 |
| --- | --- |
| Package manifest | `926a2dc2002941987c40b4ddd656e5e4b12d5c5e2fa74423fb3500b8f026631b` |
| Q4_K_M base | `9657e9d21175ed290fa4ec3662fffb61a001463ac4ec1abd62deaa509b440710` |
| V3 F32 LoRA | `f002c12aef178431cd56bc482350f3733e92f51972a668ed119a7d946e68daf1` |
| Cached official PDF | `d448cb2dd381aa1771982c5f23c92422e29c4ea283d1a7710c262bbf848a1b73` |

Checksums verified for each owned server launch. V1 package preserved, not used.
V3's previously evaluated release NO-GO status remains unchanged.

## Benchmark method and results

Intel i5-1035G1: 4 physical / 8 logical cores. CPU only; parallel 1; loopback 8096;
mmap default, no mlock; seed 17, temperature 0, max_tokens 1800; exact existing
schema/expand validator. No transport replay. Each comparison starts a fresh owned
server, leaves user/product processes alone and stops only its own child afterward.
Memory sampled every second. Stop on <128 MiB commit headroom; no old 4 GB RAM rule.
No DHC/network PDF fetch: all tests read the previously saved official PDF.

Same six-anchor operative probe on pages 9–10:

| Context / threads / cache / repack | Completion seconds | Peak private GiB | Min commit headroom GiB | Result |
| --- | ---: | ---: | ---: | --- |
| 4096 / 2 / f16 / default | 197.09 | 2.493 | 0.298 | Structured pass; current operative probe retained |
| 3072 / 2 / f16 / default | 189.03 | 2.329 | 1.066 | Same probe output as baseline |
| 2048 / 2 / f16 / default | Not sent | 2.174 | 1.156 | Rejected: another required prompt is 2138 tokens |
| 3072 / 2 / q8_0 K+V / default | 300.02 timeout | 2.143 | 1.084 | Rejected; no complete response |
| 3072 / 2 / f16 / no-repack | 189.89 | 0.694 | 2.566 | Rejected: current nonpayment misclassified Historical |
| 3072 / 4 / f16 / no-repack | 109.28 | 0.690 | 2.543 | Same scope mismatch, rejected |
| 3072 / 8 / f16 / no-repack | 103.67 | 0.695 | 2.571 | Same scope mismatch, rejected |
| 3072 / 4 / f16 / default | 155.17 | 2.357 | 0.342 | Same operative probe classifications as baseline |
| 3072 / 8 / f16 / default | 138.97 | 2.338 | 0.895 | Same operative probe classifications as baseline |

KV K+V quantization was treated as one cache-policy variable. No q4 experiment.
No-repack is runtime repacking, not requantization or changing model weights.
Numerical kernel differences changed classifications despite deterministic sampling;
JSON/schema pass was correctly not treated as oracle equivalence.

Full four-call worker, ctx3072/f16/8 threads/default repack:

| Prompt snapshot cache | Full worker seconds | Peak private GiB | Min available MiB | Min commit headroom GiB |
| --- | ---: | ---: | ---: | ---: |
| Default 8192 MiB budget | 338.69 | 3.197 | 437.9 | 0.404 |
| `--cache-ram 0` | 276.02 | 2.345 | 163.9 | 1.004 |

Cache-off returned **exactly equivalent 19 expanded facts**, with four HTTP 200,
finished/schema/semantic-validated calls: 97.87, 75.01, 79.70, 19.95 seconds.
Same-slot prefix reuse still exists; cache-ram disables snapshot storage, not every
form of prefix reuse. All prompts untruncated: 2138/1830/1834/1148 tokens. Server
logs confirm truncated=0. 3072 fits this measured source, not a universal Q&A fit.
Windows/tool access remained operational; responsiveness is observational, not a
formal UI-lag measurement. Paging persisted, particularly load-time bursts.

PDH pages-input/sec median over load+request was 1345 with default snapshots and
226 with cache off; peaks 123814 and 144938 respectively. These are system-wide
observations, not model-only paging. Baseline4096 ran before PDH was added; only its
memory/pagefault samples are available. Windows dynamically changed commit limit;
no pagefile setting was altered. Before/after samples in receipts disclose that
confounder, so timings are measurements, not a controlled universal speed claim.

## Full source-oracle mismatches: why acceptance stops

Real source W.P.(C)2493/2023, order25September2026:

1. **Petitioner relief missing:** selected page1 anchor0 contains the original
   compensation prayer under Section24(1)(a), but the model omitted it. Retaining
   a candidate is not the same as extracting it. No expected answer injected.
2. **Conditional LAC action not represented as actionable:** pages9–10 paragraphs
   17–19 survived verbatim (examine documents/process compensation in law; release
   may occur; reference if needed). Model categories are COMPENSATION_FACT and
   PROCEDURAL_EVENT. Existing `office_action` requires COURT_DIRECTION plus explicit
   `LAC shall/is directed/let LAC` wording; the result has zero actions. Existing
   imperative gate likewise does not admit these conditional/permissive expressions
   as COURT_DIRECTION. No unconditional payment/deadline fabricated.
3. Current-position selection leads with pending-applications disposition rather
   than petition disposition. Both exact facts exist, but summary prioritization
   remains unsuitable for the complete officer acceptance criterion.
4. The 2026 affidavit introduction is Historical in model output, while its actual
   LAC submission contents remain correctly quoted/attributed. Not falsely marked
   as recorded Court compliance.

Passed: current nonpayment preserved; NHAI deposit retains “is stated to have”;
LAC ownership/title position remains a submission; current paragraphs17–20 unquoted;
petition disposition present; prior2018 directions stay Quoted; six months remains
attached to adjudication, not invented as a LAC payment deadline. Every published
offline fact has exact date/page/source passage. Output remains NeedsReview with
14 unrepresented nonquoted anchors; no claim all orders/facts verified.

**Fastest measured source-faithful probe candidate:** ctx3072/f16/8 threads/repack,
with `--cache-ram 0` preventing multi-call snapshot growth. **No configuration is
accepted for full real E2E yet.** Runtime tuning cannot correct the omitted prayer
or conditional-action representation. Those require a separate correctness choice,
not weakening safeguards or relabeling gold to call this successful.

## Receipts and runtime state

All raw responses, validated facts, full order/artifact, commands, immutable hashes,
memory samples and stderr retained under:
`D:\LAC-CourtAI-V3-20261003-r1\optimization\`.

Only isolated offline benchmark artifacts written. Registered-case extraction files
not overwritten. Model8096 stopped after benchmarks; API5088/frontend5175/question
runtime8097 left running. 8097 still has pre-turn loaded Python code; it was NOT
silently promoted/restarted because full oracle failed. No real refresh/Q&A/caseB.
No live DHC request, CAPTCHA, resume, review decision or canonical mutation.

## Tests

- Python full suite: 173 pass (including boundary/preselection tests).
- Frontend: 97 pass.
- Direct Vite production build: pass; existing large-chunk warning.
- Fresh focused .NET build/test encountered DLL locks from running API7716;
  API not stopped just for tests. Existing compiled focused suite: 10 pass,
  `--no-build --no-restore --filter FullyQualifiedName~CourtIntelligence`.
- No backend/domain/schema/migration/frontend source changes.

No merge/main deployment, no model launch left unattended, no scope expansion into
OCR/retraining/connected-case tuning.
