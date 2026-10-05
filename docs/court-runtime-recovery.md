# Court runtime recovery and WPC 6328/2026 acceptance

This backend change builds on `d620f7f3f1d5e2ad7e5d82bc67c4e4d8aafa6b7f`. Court language, temporal grounding, conversation isolation, independent claim validation and GeneralLocal routing remain in place. React/CSS and model settings were not changed.

## Read-only diagnosis

The local database resolved WPC NO.6328/2026 to `2f2cfa76-1a50-43a6-81bb-62644733c190`. Its only eligible source is the Delhi High Court PDF dated **8 May 2026**, with normalized identity `delhihighcourt|wpc|6328|2026` and observation `7dc691bd-ea18-4b23-ab0c-fbfe64810207`.

The canonical root remained `C:\LAC-Platform\src\LAC.Api\App_Data\extraction`. There was no active worker OS lock or running case. Both 8096 and 8097 were offline; old PID records did not refer to live services. This was not a worktree extraction-root mismatch.

Two independent issues made the case appear stuck:

1. The saved order was explicitly blocked with `SourceBytesChanged`. Normal refresh deliberately reused the blocked record, preserving source verification rather than silently accepting changed PDF bytes.
2. The local services were stopped. Existing responses collapsed offline, busy and source-blocked conditions into a generic unavailable message; officers could not observe or recover the runtime.

The initial `current.json` checksum was `dc444bf6325b28bcf724c7e445059700a2545917e62af45f7863435079d93250`. The blocked PDF hash was `4b0039db97605897104ee800fb1b69a11a6051a9f220b7c463d796dfce9631ea`; a previously saved complete brief referenced `7a593efda4ec0bb1ef3a4ce92b5836c8de611bb3f32184b9bec0593dd8909e19`.

The official source-row evidence hash `b4b59ea9fcbbf77d7f1a0b7fe9602da3b9ee1370876b1c06ec5cb9e62869e78e` was unchanged. That hash describes the source observation, **not the PDF bytes**. Case ID, case caption, date, URL, observation, extraction/rulebook/model versions and all native source citations were checked separately.

## Explicit source-version review

There was no retained PDF matching either saved byte hash in the canonical cache or local source-document table. One necessary official retrieval supplied a local PDF with SHA-256 `997fd15c57dc1f190b05d9fded754d74bb6c6f77707de3f5b451db8b22fd0642`.

The four pages had the exact case caption and date. All **20/20 selected native paragraphs** matched the earlier complete brief verbatim. The old bytes were unavailable, so the reason for the container-byte difference cannot be established; content equivalence was proved only for the complete selected native evidence and caption/date coverage.

`source_version_review.review_cached_selections` is an explicit local review utility, never an automatic refresh bypass. It requires the earlier complete publication, matching versions and source-row provenance, complete identical candidate coverage and exact caption/date. It replays saved model selections through the existing worker's independent validation and synthesis against the newly hashed local source. New/missing native candidates, altered caption, different row evidence, incomplete coverage or changed versions fail closed.

The reviewed candidate passed independent ASP.NET artifact validation before atomic publication under the existing worker lock. Old files were preserved externally. There were **zero V3 model calls**. Normal refresh still blocks changed bytes; no verification rule was relaxed. The record retains `NeedsReview` for seven uncertain selections while its **13 usable facts** support **1/1 AI-ready briefs**.

Publication began at `2026-10-04T23:04:06.364473Z` and the first usable brief was published at `2026-10-04T23:04:14.796495Z`: **8.437 seconds**, or 05 October 04:34:06–04:34:14 IST. This measures local revalidation/publication, excluding the earlier diagnosis and necessary PDF retrieval. Accepted WPC 6203/2026 artifact checksums were unchanged.

## Runtime/status API contract

All routes retain authenticated case-view authorization and `Cache-Control: no-store`:

- `GET /api/court-cases/{id}/intelligence`: existing intelligence plus `runtime`; existing `progressSummary` gains runtime/action fields.
- `GET /api/court-cases/{id}/intelligence/runtime`: bounded machine-readable runtime and case progress.
- `POST /api/court-cases/{id}/intelligence/refresh`: returns existing same-case processing state (202), an explicit competing worker/case (409), or unavailable/configuration diagnostics (503).
- `POST /api/court-cases/{id}/intelligence/runtime/recover`: explicit configured application recovery (202); configuration unavailable/invalid fails closed (503). Repeated recovery does not start a second in-process recovery; an OS lock also guards separate API instances.

`runtimeState` is one of `Ready`, `Starting`, `Processing`, `BusyWithOtherCase`, `ModelOffline`, `QuestionServiceOffline`, `CaseNotReady`, `SourceBlocked`, `Failed`. Component fields `modelState`, `questionServiceState` and `caseState` distinguish a usable cached brief from unavailable inference. A ready case can therefore coexist with an offline model.

Progress reuses the existing per-case `refresh.json`: `status`, `checked`, `total`, `processingCurrentOrderDate`, `startedAt`, `completedAt`, `elapsedSeconds`, `reasonCode`, and an officer-safe `message`. Completed work survives process restart. A stale lock file alone is not busy; the actual OS lock is probed. Lost running sessions are disclosed as interrupted. No prompts, raw exceptions, credentials or another case's ID are returned as diagnostics.

With zero usable briefs, the response and progress explicitly expose `actionStatus: UnavailableUntilVerifiedIntelligenceReady` and “Action status unavailable until verified intelligence is ready.” Grounded Q&A cannot turn missing evidence into verified absence of an action.

## Controlled local lifecycle

Recovery is disabled until the host configures absolute existing paths and SHA-256 pins under `CourtRuntime`:

| Setting | Purpose |
| --- | --- |
| `RecoveryEnabled` | Explicit host opt-in |
| `PythonExecutable` | Installed local Python executable |
| `RecoveryScript`, `RecoveryScriptSha256` | Exact reviewed recovery helper |
| `ManifestPath`, `ManifestSha256` | Existing pinned V3 manifest |
| `PackageSha256` | `runtime_recovery.package_digest` of sorted Python package filenames and file hashes |
| `ModelVersion` | Expected verified question-service model version |
| `RuntimeDirectory` | Existing absolute local folder for bounded lifecycle record, PID records and stdout/stderr |

`Storage:ExtractionRoot` must remain the canonical existing root. Health validates its fingerprint, question-package digest and model version. Hash pins must be regenerated and deliberately configured when the reviewed package changes; a mismatched service is not reused or killed automatically.

The helper verifies the manifest, base GGUF, LoRA, llama-server and all manifest runtime-file hashes before starting services. It binds only `127.0.0.1:8096/8097`, retains CPU-only settings (3072 context, 6 threads, parallel 1, GPU layers 0), and has no cloud fallback. Services start hidden. Package verification/startup is observable in a small global recovery record; case progress remains in the existing case sidecar.

Q&A starts independently so verified cached Court answers remain available when inference cannot load. Model-offline Court Q&A uses bounded existing verified facts and the same claim/citation/language/temporal guards. It makes no model call and does not make assistant history factual evidence. GeneralLocal generation and processing new sources still require the model.

The first pinned model start failed because Windows could not allocate its **1,765,048,320-byte CPU_REPACK buffer**. The recovery reason is now `ModelInsufficientMemory` with a safe recovery message. The accepted model/settings are not reduced to hide that host limitation. A nested health timeout was also corrected: the internal model probe is shorter than the outer question-health probe, preventing an alive Q&A service from being reported as a startup timeout.

## Evidence files and validation

Local diagnosis/publication evidence is under `D:\LAC-Court-Runtime-20261005`: `runtime-before.json`, `case-index.json`, `case-api-before.json`, `source-version-check.json`, `pre-publication-validation.log`, `publication-proof.json`, `preserved-source-blocked`, and subsequent authenticated acceptance responses. Raw PDFs/runtime logs are not committed to Git.

Validation covers source-version mismatch rejection, complete native review, offline cached grounding, zero-evidence action semantics, same-case refresh reuse, other-case busy behavior, stale OS locks, interrupted/failed progress, recovery pin requirements and runtime-root mismatch. Host memory limitations require sequential test batches with bounded GC and compilation concurrency; unchanged Infrastructure/Domain build artifacts are reused and build analyzers are disabled for this constrained local verification.

The final Python suite passed **275 tests**. The focused .NET Court/DHC suite passed **234 tests**, in 16 sequential class batches with zero failures or skips. The final API build had zero errors and warnings. An earlier combined .NET rerun encountered host memory exhaustion; its log is retained separately from the successful final batch results. The offline HTTP regression also proves that retry wording cannot trigger a PDF fetch or model call while inference is unavailable.

Final authenticated verification ran against the activated local API on **5088**. WPC 6328 returned `usableAiBriefs=1`, `blockedBeforeAi=0`, `latestBriefReady=true`, populated `latestOrder` and cited `currentPosition`. The seven uncertain selections remain under review; completeness is not overstated. WPC 6203 still returned eight usable briefs and zero blocked sources.

Hinglish, English and Hindi Court Q&A each returned HTTP 200 with independently accepted claims tied to the 8 May PDF URL/date. Original evidence quotes remained verbatim. `chatbot-proof.json` and `final-live-proof.json` retain the authenticated responses. A subsequent refresh returned 202 and durably completed its 1/1 cache check in **0.984 seconds**, with identical facts/source hash, no model calls and unchanged accepted 6203 checksums (`refresh-reuse-proof.json`).

The final controlled recovery ran from `2026-10-05T05:32:24.461766Z` to `2026-10-05T05:32:37.000248Z`. **8097 is Ready; 8096 is ModelOffline** after the pinned CPU model again failed memory allocation. API `runtimeState=Failed` and `reasonCode=ModelInsufficientMemory` coexist explicitly with `caseState=Ready`. Cached grounded Court Q&A works; new inference and GeneralLocal free generation remain dependent on recovering sufficient host memory. The model was not downloaded, reconfigured or rerun for order extraction. The officer recovery endpoint can retry after memory is freed.
