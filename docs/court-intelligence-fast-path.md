# Court Intelligence fast path acceptance

Implemented on `codex/court-intelligence-v1-office-pilot`, starting from accepted
`57851355e22857a1cde152036d6117de7f335ba7`. Measurements were taken on 4 October
2026 on the existing 8 GB CPU machine with the accepted V3 model and deep-runtime
command. No model, adapter, runtime configuration, source validation or action
safety policy was replaced. Main was not merged and IIS was not deployed.

## Measured first useful result

Actual registered matter: **WPC NO. 6203/2026**, Yudhvir Singh,
`665ec7ce-c31b-4320-9c74-45b69e14327a`, eight discovered official PDFs. All eight
already had accepted intelligence. Profiling used isolated copies of its actual
23 September 2026 native-text order; it did not erase or reprocess live artifacts.

[Official measured order](https://delhihighcourt.nic.in/app/showlogo/1790336524_9e38a72e640937d2_pms_62032026.pdf/2026)

| Stage | Before, seconds | After, seconds |
|---|---:|---:|
| Official PDF download | 7.166 | 0.340 |
| Native text parsing | 0.066 | 0.029 |
| Anchor/candidate creation | 0.027 | 0.017 |
| Prompt/schema construction | 0.746 | 0.692 |
| V3 prompt evaluation | 57.653 | 22.909 |
| V3 generation | 21.183 | 27.022 |
| Semantic validation | 0.064 | 0.140 |
| Completeness/recovery checks | 0.040 | 0.176 |
| Synthesis/persistence | 0.063 | 0.031 |
| First useful verified publication | **87.440** | **51.530** |
| Final isolated order completion | 87.440 | 51.725 |

Stage timings have different boundaries: inference server prompt/generation
timings sit inside the measured provider round trip; validation occurs inside
extraction/expansion. They are not additive accounting buckets.

The final fast run used **one V3 request**, 462 prompt tokens and 104 generated
tokens, returning four individually checked facts across both pages. All four
selected anchors were represented, with no completeness retry or operative
omission. The application disposal, appearance correction and 9 October listing
were included. Application disposal was not treated as disposal of the petition.
The baseline also used one call on this short order (555/127 tokens); this change
does not claim that every previous order required multiple calls.

An earlier compact-prompt trial returned only a context fact, then needed
operative recovery. That trial was rejected as a first-useful-result success.
The final path prioritizes operative anchor IDs and runs completeness checks
before publication.

The remaining dominant costs are **generation, 27.02 seconds**, and **prompt
evaluation, 22.91 seconds**. Download/cache/runtime conditions differed between
the two measurements. This is one controlled real order, not a guarantee that
every cold, long or recovery-heavy order will finish within 60 seconds.

## Page open, discovery and eight-source behavior

- Authenticated actual case reload to the loaded Court AI section: **1.182
  seconds**, with the model process stopped during the check. All eight official
  PDF links and eight usable briefs were visible, including two briefs requiring
  review. Latest verified order remained 23 September; listing remained 9 October.
- Reading the accepted eight-source artifact took **0.021875 seconds**. Explicit
  cached refresh on an isolated copy took **0.128452 seconds**, with **zero V3
  requests and zero PDF downloads**. Complete reviewed artifacts also reused.
- Controlled discovery-to-read-model check: supply the real eight-source index
  to a copy containing seven accepted older briefs. The API exposed all eight
  official links in **0.182764 seconds**, before any PDF download or inference;
  the new latest source was pending. This measures the publication boundary
  after DHC metadata discovery. Fresh DHC network/CAPTCHA discovery was not
  retimed and is not included in that number.
- Controlled first-publication API check using the real measured fast brief:
  eight links, one usable brief, `latestBriefReady=true`, checked `1/8`, current
  processing date 23 September, `backgroundProcessing=true`,
  `coverageComplete=false`, and legacy `processingComplete=false`.
- A fixture-backed continuation check publishes the completed fast batch after
  request one, then processes remaining anchors in request two on the same
  temporary PDF. Interruption/resume checks retain that batch and classify only
  the remaining anchors. Separate latest-first case tests continue older sources
  newest to oldest while each published history remains chronological.

A fresh eight-PDF historical inference run was not performed against the live
matter because all eight artifacts were already accepted. Background
continuation is covered by controlled worker tests, interruption/resume tests and
the real latest-order copy. Existing live artifact SHA-256 remained:

`9a7f7e3d2ee55acc264373d73d45e5d8c03619061e24388f13a95cfbb4859b51`

Local measurement records and screenshots are in
`E:\LAC-DHC-Fast-20261004`: `before-profile.json`, `after-profile.json`,
`after-first-ready.json`, `actual-cache-check.json`, `actual-eight-case-open.json`,
`discovery-control-api.json`, `fast-publication-control-api.json`,
`cached-eight-source.png`, and `verified-latest-brief.png`.

## Processing and evidence behavior

The registered full-history worker explicitly sorts unprocessed exact sources
newest first. It reads reusable per-source artifacts before scheduling and
downloads/processes one selected PDF at a time. Unchanged validated, completed
reviewed and source-blocked records reuse only matching publication identity,
model/extraction/rulebook versions and checked PDF hash. A changed publication
hash cannot be hidden by an unchanged observation ID. Ordinary page opens and
ordinary questions do not fetch pending historical PDFs or call V3 implicitly.
Nineteen unchanged sources plus one new source process only the new source.

Fast selection retains exact original anchors and every unquoted operative
passage, including overflow beyond the target six-anchor pack. Bounded chunks
may require more than one request; operative evidence is not silently truncated.
Compact prompts remove redundant bookkeeping. The original schema, anchor
expansion, page/fragment evidence checks, quote/speaker attribution and
conditional-action guards still decide which facts are usable.

Only a completed selected batch with no unresolved selected high-signal anchors
can be atomically published. A context-only response with an unresolved operative
passage does not trigger fast readiness. It explicitly has
`briefTier=Fast` and `deepProcessingComplete=false`, with completeness checks and
omitted anchor IDs retained. The same PDF then supplies deep enrichment; no
second temporary PDF or concurrent inference is started. A smaller fast record
cannot replace richer checked evidence of the same bytes and versions. Failed
enrichment retains the complete verified fast batch with a review message;
failed partial chunks never become usable evidence.

During background inference, Court Q&A answers immediately from checked
extractive evidence and discloses pending coverage. An exact pending historical
date returns `OrderProcessing`, with no claims; a blocked unverified date returns
`OrderNotVerified`. Busy inference for another case does not falsely claim that
this case's history is running.

## Stable API progress contract

`progressSummary` is API-owned and provides:

| Field | Meaning |
|---|---|
| `officialSources` | Registered discovered source count |
| `usableBriefs` | Sources with individually verified usable facts |
| `blockedSources` | Sources blocked before AI |
| `pendingSources` | Waiting/processing sources plus unfinished enrichment |
| `latestBriefReady` | The latest known date has a usable brief |
| `latestOrderDate` | Latest known dated official source |
| `processingCurrentOrderDate` | Indexed order date currently being processed |
| `processingChecked` / `processingTotal` | Worker progress across registered sources |
| `backgroundProcessing` | This case's refresh worker is running |
| `coverageComplete` | All indexed sources checked without pending work or review |

Usable, reviewed and pending counts can overlap: a verified fast brief remains
pending for deep enrichment. Coverage never becomes complete just because the
latest brief is ready. Minimal UI changes display readiness and background
progress; the upcoming visual redesign can consume these fields directly.

## Validation

- Full .NET: **807 passed**, zero failed/skipped.
- Full frontend: **114 passed**, zero failed/skipped.
- Focused Python regression suite: **98 passed**.
- Full Python: **247 run, 242 passed, five errors**. All five errors are
  `test_model_startup` PowerShell subprocess timeouts on this host. No tests were
  skipped and no startup/disk reserve guards were weakened. The packaging tests
  passed after deleting only the superseded task-owned test build output.
  The final full run used the accepted `run_python_tests.py` harness with the
  configured local `gguf-py` dependency and test temp directory, with the model
  process paused for memory headroom. An earlier recheck using default Python
  missed that dependency path; a model-loaded attempt exhausted memory. Those
  failed attempts were resolved before this final result and are not reported
  as successful validation.
- Production Vite build passed. TypeScript completed with **39 existing
  diagnostics**, matching the accepted baseline after normalizing line numbers;
  **zero new diagnostics**. Full TypeScript is therefore not clean.
- Whitespace diff checks passed. Accepted model command equality, local model
  health and absence of temporary PDFs were checked after restoring the runtime.

Logs are in `E:\LAC-DHC-Fast-20261004`. Existing xUnit analyzer and bundle-size
warnings remain. The PowerShell startup suite limitation and existing TypeScript
diagnostics are explicitly retained in this acceptance record.
