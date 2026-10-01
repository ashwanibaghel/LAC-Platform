# Court Order Intelligence Phase 1 — local acceptance

Date: 2026-10-02. Branch: `codex/court-order-intelligence-phase1`.
Base: `6d814b13c588c680184197c6cfb77543f4cd90ee`.
Research checkpoint: `7c62ee3`. Implementation checkpoint:
`914951c93541ae531cf0ab318f82d5e59dbb86e5`.

## Acceptance result

The narrowed Phase-1 acceptance passed on a real official LAC-direction order.
This is local acceptance, not office deployment or universal PDF coverage.

W.P.(C) 8664/2021, order dated 2025-01-30, page 6:
“The LAC is directed to forward the petitioner’s reference as expeditiously as
possible, preferably within a period of four weeks from today, to the concerned
court.”

Official source:
https://delhihighcourt.nic.in/app/showFileJudgment/VIB30012025CW86642021_112417.pdf

Source SHA256:
`a0ddb33e46ce9d1d6d017dd715da57cf57afb0a6e677fa74c91318de721f84a5`.

The native-text pipeline and actual local Qwen inference selected the current
Court direction, retained its exact wording, actor, date and page, and generated
one Reference action. The preference qualifier remains visible. Its arithmetic
date is 2025-02-27, not a claimed unconditional statutory deadline. Completion
remains unconfirmed; silence never closes it. No next hearing was invented.

An unrelated uncertain passage keeps the source review-marked. Independently
validated facts remain usable only after all selected chunks finish successfully;
uncertain passages and conflicting compensation/possession fields are withheld.
Partial/failed extraction cannot create actions. A petition filing is not recorded
compliance of an earlier obligation.

The officer asked “What does LAC need to do before the next hearing?” using the
running local model. The UI returned this exact Court direction with order date,
page 6, expandable source passage and official link. No cloud inference was used.
Current position, latest order, actions and collapsed timeline rendered together.

Screenshot outside Git:
`C:/Users/ashwa/AppData/Local/LAC-CourtIntelligence/phase1-real-lac-direction-qa.jpg`.
Preview: `http://127.0.0.1:5176/court-intelligence-demo.html`.
This is an isolated development-only public-order demo, not the office register.
Registered Court Matter integration uses authorized API access and that matter's
existing extraction-root artifact; setup is documented in the tools README.

## Tests and checks

- Native extraction/attribution/deadline/Q&A/cleanup/AI-off safety: 58/58 passed.
- Focused .NET Court Intelligence artifact tests: 2/2 passed.
- Full .NET suite in this implementation: 761/761 passed, no failures/skips.
  C# was unchanged after that full run; the final focused run also passed.
- Frontend tests: 74/74 passed.
- Direct Vite production build: passed (173 modules; existing large-chunk warning).
- PowerShell start/stop syntax checked; actual Questions start/stop/restart passed.
  PID ownership checks work with both JSON string and DateTime timestamp decoding.
- `git diff --check`: passed.
- Local model file SHA matched its publisher LFS SHA. Literal loopback only;
  proxy/redirect/cloud fallback disabled, CPU-only, one inference slot.
- Temporary PDF cleanup tested on success/failure/cooperative stop. Seven copies
  left by earlier interrupted development attempts were removed from task-specific
  temporary folders after checking their contents. No permanent PDF was retained.

The standard `tsc -b` still has pre-existing errors in unrelated application,
Award/editor/timeline code. Direct Vite build passes; unrelated type-check fixes
were not included in this phase.

## Coverage and remaining limits

Fifty unique official PDFs were studied and annotated before prompt/rulebook work.
Runtime demonstrations attempted five multi-order matters plus this single-order
LAC-reference case: 15 source attempts, not 15 successful extractions. At the
final snapshot, 2 sources were Validated, 3 NeedsReview (including the real
reference order's usable verified direction), and 10 NeedsSourceReview.

Scanned/unusable native text, connected-case captions requiring case-specific
attribution, unsupported caption/body layouts and unavailable direct PDF links
fail closed. No OCR or guessed connected-case obligations were added. An
incomplete/review-marked matter is explicitly not a complete account of duties.
Q&A is bounded, current-matter-only and extractive: it selects validated evidence
and returns exact attributed text, never ungrounded model prose. Unknown facts
return “Available Court orders do not establish this fact.”

Court Intelligence is optional. AI service failure leaves stored intelligence and
the rest of the Court Matter page available. No automatic model/DHC action occurs
on page render. No assisted search, CAPTCHA, sync or historical action was run.
Only known direct public PDFs were downloaded for study/demo.

No canonical Court data, Award Intelligence, domain/schema/migrations, office DB,
IIS deployment, main branch or existing dirty source worktrees were changed.

## Setup and changed areas

See `tools/court-order-intelligence/README.md` and
`docs/court-order-intelligence-rulebook-v1.md`.

Changes are limited to the study/worker/provider/semantics/Q&A tools and tests,
local-runtime scripts, authorized read/ask API adapter, Court Matter Intelligence
panel/styles/tests, and separate development demo configuration. Runtime model,
PDFs, extracted text, logs, screenshots and intelligence files stay outside Git.
