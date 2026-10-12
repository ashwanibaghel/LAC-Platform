# Anti continuation — preserved UI on the tested backend checkpoint

Backend checkpoint: **`7bbec8053170ad9c710adb312e9e7a2d3810f95d`**, branch `codex/land-records-v2-integration`.

Anti continuation branch: `codex/land-records-v2-ui-continuation`. Its parent is exactly the backend checkpoint. This branch contains the preserved UI work and handoff; it is not a main merge or deployment.

| Owner/purpose | Exact worktree |
| --- | --- |
| Codex backend only | `C:/Users/ashwa/.codex/worktrees/land-records-v2-verify/LAC-Platform` |
| Anti frontend continuation | `C:/Users/ashwa/.codex/worktrees/land-records-v2-ui/LAC-Platform` |
| Original UI preservation/archive reference | `C:/Users/ashwa/.codex/worktrees/land-records-v2-integration/LAC-Platform` |

The original preservation branch is `codex/land-records-v2-ui-preserved`. First immutable draft snapshot: `9ebdad74c58d7b6195b26e2797b73b6554bdd74f`. Anti subsequently committed its cleanup/report/evidence at **`4ed6bdae1e9c346924acb55e20b686fbf312d936`**. Both are retained and the latest source was used here, so the later valid edits were not lost.

## Individually reviewed preservation

Restored only the five Award/temporary UI modules, their reviewed App.tsx imports/state/panel/modal wiring, the temporary-panel import/render hunk in VillageCoreRecordsWorkspace, and Anti's original report/screenshots. No unrelated commits or backend files from the preservation branch were copied. App's old notification/document view replacement and refresh callbacks were reviewed against the canonical API. The temporary panel remains a separate proceeding workflow. The lifecycle revision fallback was corrected to use `??`, preserving valid revision zero instead of sending revision one.

The original UI files, their exact changes and the initial mixed shared-file hunks remain accessible through the immutable preservation commits. No reset/clean/discard was used on the original worktree. This continuation preserves the audited premium frontend stack already present in the backend integration plus the later UI drafts. The original Anti source branch `anti/land-records-premium-ui-v2` was not rewritten.

Canonical contract: [LAND_RECORDS_V2_INTEGRATED_API_CONTRACT.md](LAND_RECORDS_V2_INTEGRATED_API_CONTRACT.md). Backend evidence and migration/concurrency limitations: [LAND_RECORDS_V2_INTEGRATION_VERIFICATION.md](LAND_RECORDS_V2_INTEGRATION_VERIFICATION.md). Original Anti report under its original filename is retained as author evidence, not substituted for these independently scoped verification results.

## Checks after UI incorporation

Forced TypeScript check: **37 existing diagnostics**, compared with **38 on the backend checkpoint**. No added diagnostics; the removed diagnostic is the retained legacy AwardIngestionReview compatibility reference. The whole repository TypeScript build remains failing in unrelated shell/Court/Matter/editor code. Vite production bundle passes. Backend code and migration operations are unchanged from the tested checkpoint.

The actual-browser run against the exact backend checkpoint and this frontend worktree passes the preserved P0 officer correction, grouped chronology/source links, premium Village Direct upload, Smart proposal confirmation and an actual metadata date correction. **1/1 passed**, `preserved-ui-browser-final.trx`, **56 seconds**. Test fixture explicitly binds `LAC_STAFF_WEB_ROOT` to this worktree and asserts the preserved metadata UI exists; it does not silently test the backend-only frontend. The metadata save result is read back from the actual authenticated API. The result records the exact frontend root; artifacts are under ignored `out/browser-checks/` here and the TRX is in the exclusive backend checkout.

These checks do not certify complete staff acceptance of all draft forms. Anti still must run the full metadata/lifecycle/notification/temporary UI scenario matrix, permission-limited users, revision conflicts, and real office source/OCR/UAT. In particular, notification UI currently receives the first linked Village from App.tsx; multi-Village Awards need an explicit validated Village selection rather than assuming that default. Award-only officer ownership correction must not be used to retarget existing NM review history. Preserve the backend's protected verified metadata and source-role policies.

## Coordination boundary

Anti must edit only the dedicated frontend continuation worktree/branch. Codex owns the backend checkout. Do not share dependency installations or build outputs. Pause/finish any external Antigravity task still pointed at the original preservation checkout before switching it to this directory. Codex cannot remotely interrupt that external agent through the available app tools; no IDE process was killed, avoiding loss of unsaved buffers. No concurrent edits were observed in the final Codex checkout.

Further UI commits should remain on this continuation branch. Re-run appropriate UI/API integration checks for each shared-file conflict or behavior change. Keep every preserved source commit; do not wholesale merge the old Anti history. Main, protected databases/accounts, source PDFs and IIS are outside this checkpoint's mutation scope.
