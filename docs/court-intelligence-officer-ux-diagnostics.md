# Court Intelligence officer UX and source diagnostics

Implementation continues from `90750bf865562d0e2daa969206723a352c7957e3` on `codex/court-intelligence-v1-office-pilot`, in the accepted office-pilot worktree. No main merge or IIS deployment. The accepted V3 weights, adapter and runtime command are unchanged.

## Exact second matter

Read-only inspection of the current local database identified **Ramesh Vs. District Magistrate (South-West) & Ors., WPC NO. 6328/2026**, case ID `2f2cfa76-1a50-43a6-81bb-62644733c190`. It is the most recent one-source matter; the other one-source case is the preserved Prem matter.

| Actual observation | Stored value |
|---|---|
| Observation ID | `7dc691bd-ea18-4b23-ab0c-fbfe64810207` |
| Normalized identity | `delhihighcourt\|wpc\|6328\|2026` |
| Raw case number | `W.P.(C) 6328/2026` |
| Parsed order date | `2026-05-08` |
| Raw order date | `08/05/2026` |
| Evidence SHA-256 | `b4b59ea9fcbbf77d7f1a0b7fe9602da3b9ee1370876b1c06ec5cb9e62869e78e` |

The saved row evidence contains the raw case number, date and [official PDF](https://delhihighcourt.nic.in/app/showlogo/1778324702_50f27990d32c835d_svn_63282026.pdf/2026). Its source provenance is the approved status-result Orders route, `case-type-status-details`.

**Original rejection: `CaseIdentityMismatch`.** The API compared `wpc63282026` from the normalized observation with `wpcno63282026` from the registered display number. `NO.` was incorrectly treated as part of the case type. The date and URL were present and eligible. The misleading unavailable-order state followed from filtering out that otherwise valid row.

The API now uses the existing canonical Court import identity parser. Python likewise removes the optional display `No.`/`Number` label immediately before a numeric case/year pair. The registered GUID, forum, case type, number and year must still match exactly. Generic regression tests reject different numbers, GUIDs and forums. No matter-specific implementation branch was added.

The actual API view admitted the row as `Eligible / Waiting`. An initial live source-only retrieval timed out before PDF verification. This was a separate retrieval condition, not evidence that the date or case identity was unsafe. Download timeouts/unavailability now have typed source diagnostics and withhold facts before AI; they do not masquerade as incomplete AI extraction.

The subsequent actual refresh **completed successfully with review**: source verification passed, all four pages and four selected chunks were processed, all 20 selected anchors were represented, and no worker failure remained. The actual API exposes **13 individually usable facts**, one usable brief, zero blocked sources and one processed brief with review. The generic identity fix therefore reaches real PDF processing; it is not merely an eligibility/unit-test fix. The downloaded PDF SHA-256 was `7a593efda4ec0bb1ef3a4ce92b5836c8de611bb3f32184b9bec0593dd8909e19`; its temporary PDF was cleaned after completion.

## Officer-facing behavior

Every discovered source has API-owned `sourceDiagnostics`: date/raw date, PDF URL, observation ID, source state, reason code, officer message, AI state, usable fact count and expandable technical provenance. Known legacy worker failures are adapted in the backend. The frontend does not infer reasons from failure text.

`pipelineSummary` separates official sources, usable briefs, sources blocked before AI, completed briefs with review, pending processing and incomplete extraction. A reviewed usable brief is explicitly disclosed as part of the usable count. The UI uses one primary notice, a compact official/office/date/sync strip, then current position, LAC actions, latest verified order, Court AI and full source history. Blocked PDF links remain visible; their facts remain withheld.

Explicit English, Hindi and Hinglish LAC-action requests use deterministic intent handling and existing evidence/lifecycle guards. Mandatory directions retain citations; conditional directions retain their conditions and non-mandatory labels. Missing LAC actions in usable evidence produce a limited conclusion, with a separate metadata-based coverage warning. Zero usable evidence is explicitly distinguished. An exact-date question that lazily processes a known PDF validates its answer against the newly published artifact.

## Actual local data checks

The actual compiled API view was run read-only against the registered source indexes and configured case artifacts. These are backend observations; they do not substitute for authenticated screen acceptance.

| Matter | Official sources | Usable briefs | Blocked before AI | Processed with review |
|---|---:|---:|---:|---:|
| Kamal | 3 | 1 | 2 | 1, included in usable briefs |
| Ramesh, the second matter | 1 | 1 | 0 | 1, included in usable briefs |
| Dayanand | 19 | 15 | 4 | 1, included in usable briefs |
| Prem | 1 | 1 | 0 | 1, included in usable briefs |

Kamal's 6 April and 18 May PDFs are `ConnectedCasePdf / BlockedBeforeAI`, with zero usable facts. The 28 September PDF is `AiProcessedWithReview / ProcessedWithReview`, with 11 usable facts. Every official PDF remains in its source card.

Dayanand retains all 19 sources: 14 processed without review, one processed with review, and four source-blocked. The 5 October 2015 PDF has `SourceBytesChanged`; 1 April, 7 August and 18 November 2025 have `ConnectedCasePdf`. The 28 July 2026 brief retains extraction review. None was reprocessed by this task.

Prem was not refreshed or reprocessed. Its accepted artifact SHA-256 remains `CF4C893725F00B4C57D6958C4F0BBF2CFEFE7503F782EFED4B7A69F9E808BDDC`.

The actual loopback Q&A service answered **“What to do LAC Branch Right now?”** for Kamal:

> No verified LAC-specific mandatory action is established in the currently processed evidence.
>
> Some discovered orders are still under source review, so no conclusion is drawn from those sources.

It returned `LacActionNotEstablished`, `CourtGrounded`, `insufficientEvidence=false`, and zero order-derived claims. These two statements describe evidence coverage, so they do not introduce an uncited Court factual assertion.

## Authenticated screen acceptance

The officer completed normal login. All four matters were then checked in the actual local app at **1366×768**, using the compiled API and unchanged accepted V3 model command. No authentication bypass or synthetic screen was used.

- **Kamal:** all three PDF cards are visible together with dates, distinct reason badges, usable fact counts and PDF links. The April/May cards explain connected-case attribution and withheld facts; September exposes 11 usable facts and completed extraction with review. The exact action question returns the two coverage-aware statements quoted above, without the generic insufficient-evidence answer.
- **Ramesh:** the actual screen shows one usable brief, zero blocked sources, 13 usable facts, the 8 May PDF and completed extraction with review. Official/office Pending and 7 October listing remain separate from the verified order date. The action panel gives the limited no-mandatory-action conclusion; it does not describe completed reviewed extraction as processing failure or establish completed office compliance.
- **Dayanand:** all 19 PDF links remain visible, with 15 usable briefs and four blocked sources. The 2015 source-byte change is distinct from the three connected-case blocks. The reviewed usable brief is explicitly included in the 15. Official listing 29 January 2027 and office NDOH 28 September 2026 remain separate.
- **Prem:** Q1–Q8 were submitted through the authenticated UI against the preserved artifact. Party positions remain attributed; Court findings remain separate; conditional permissions retain their conditions and page citations. Six months refers to adjudication of the reference, not an unconditional compensation-payment deadline. The hearing question does not invent a next date. A legacy duplicate answer warning that wrongly called the verified reviewed source unverified was removed; review remains visible in the primary notice and source card.
- **Isolation:** actual Prem → Kamal → Prem navigation clears earlier answers and the question field on each case. Kamal returns its own limited action conclusion; returning Prem retains its own disposition and conditional evidence. Existing delayed-response isolation tests also pass.

| Prem question | Actual observed answer boundary |
|---|---|
| Is case ka poora scene batao. | Attributed petitioner/LAC/other-party positions, recorded compensation, claim permission and disposal; cited pages 1, 9 and 10 |
| Petitioner kya maang raha hai? | Compensation prayer, explicitly a petitioner submission, page 1 |
| LAC ka stand kya hai? | Affidavit/ownership position, explicitly LAC submissions, pages 8–9 |
| Court ne kya find/direct kiya? | Conditional directions and disposal, pages 9–10 |
| Last 3 orders me kya hua? | Only evidence from the single available 25 September order; no invented three-order history; historical quotation remains labelled |
| Kaunsi direction abhi active hai? | Three conditional directions, pages 9–10; no promotion of quoted older directions |
| Agli hearing se pehle LAC ko kya karna hai? | Explicit conditional/non-mandatory conclusion and coverage note; three cited conditional directions |
| Next hearing kab hai? | Unconfirmed from processed orders; no invented hearing date |

Saved actual viewport screenshots and question transcripts are in `E:\LAC-DHC-UX-20261004`: `kamal-three-sources-1366.png`, `ramesh-health-1366.png`, `ramesh-source-1366.png`, `dayanand-health-1366.png`, `dayanand-source-review-1366.png`, `prem-conditional-answer-1366.png`, `prem-hearing-answer-1366.png`, and `prem-q1.txt` through `prem-q8.txt`. The screenshot captures are normal scrolled views of the actual page. The viewport override is reset after acceptance.

## Validation

- Full .NET: **802 passed, zero failures/skips**, including source diagnostics, identity format rejection boundaries and lazy citation publication.
- Full frontend suite: **113 passed**, including the final verified-source/empty-hearing warning regression.
- Full Python: **236 run; 231 passed, five errors**. All five are existing PowerShell model-startup tests timing out before script execution. A direct `pwsh -NoProfile` console probe also hangs. No tests were silently skipped, and no security protection or model startup flags were changed to work around the environment.
- Focused latest Python officer-action/worker suite: **35 passed**.
- Vite production build passed. TypeScript has **39 baseline and 39 current diagnostics**, with zero normalized diagnostic delta; it is not a clean TypeScript build.
- `git diff --check` passed. Existing bundle-size and xUnit2031 warnings remain.

The final Vite build and TypeScript baseline comparison were repeated after the actual-screen wording fixes. Only the owned acceptance model process was briefly paused for heavy compilation and restarted with the exact accepted command. Prem, Kamal and Dayanand artifacts were not reprocessed; Prem's SHA remains unchanged. The final commit SHA is reported after commit and push to the existing feature branch.

Changed areas are the API source/coverage projection and canonical identity validation; typed worker diagnostics and generic action intent/response handling; the officer UI and related regression tests. Local diagnostic metadata, API view snapshots, exact Q&A responses, screenshots and test logs are retained outside Git at `E:\LAC-DHC-UX-20261004`. No credentials or CAPTCHA answers were written to the report or committed. Full Python and TypeScript environmental/baseline limitations are disclosed above; their results are not represented as clean passes.
