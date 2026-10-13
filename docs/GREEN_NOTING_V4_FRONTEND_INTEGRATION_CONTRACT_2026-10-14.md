# Final V4 officer workspace contract

Approved base: `5bbd843dc4e5de9fc0d38bf8451b6072d8b31a97`.
Branch: `codex/green-noting-eoffice-workspace-v4`.
This extends ContinuousGreenEnvelopeV2; the official note ledger is unchanged.
Owner UI review is required. This is not a merge or deployment authorization.

## Officer entry and shared context

Matter Overview has one **Green Noting** entry into
`/matters/:id/noting-v4`. The workspace reads real authenticated Matter context,
working draft, capabilities, authorized documents, Dak details and transfers.
It resolves the only linked Dak automatically. Multiple Daks require selection;
the active editing lease prevents changing its controlling Dak in place.

The old V3 simulated prototype is removed from application routing. Its source and
previous evidence remain intact. Existing V1 API compatibility and letter draft
workflows remain available; they do not create a second official note ledger.

Left: one actual native ONLYOFFICE V2 continuous document containing locked official
history, locked officer context and one editable current-note control. Right: the
selected original authorized PDF, with its actual content endpoint, version and
filename. No fabricated PDFs, authentication, notes or textarea editor fallback.

Split View, Noting Focus, Document Focus, a pointer/keyboard divider and independent
scrolling preserve the native session. Viewing the Legal PDF overlays its owned
viewer without destroying a live editor. Canonical Review docks on the left so
the original PDF remains visible and usable on the right.

## Save and server truth

Use the published [no-change contract](GREEN_NOTING_V4_SAVE_CHECK_CONTRACT_2026-10-14.md).
Every Save exports/synchronizes actual native DOCX after blocking local input.
Docs' own “All changes saved” footer is not an application committed receipt.

1. `GET /api/noting-office/sessions/{s}/config` rechecks the live lease/revision.
2. `POST /api/noting-office/sessions/{s}/save-check`:
   quoted current `If-Match`, UUID `Idempotency-Key`, `{nativeExportUrl}`.
3. Unchanged content reuses the exact verified original receipt. Display
   **No new changes to save.** Never increment its generation or invent a commit.
4. An unchanged version-0 server seed has no committed callback receipt. Display
   **No new changes to save. Server snapshot restored.**
5. Changed content calls the existing Save endpoint with
   `{clientGeneration,nativeExportUrl}`, exact revision and held UUID/payload.
   Await the matching real Committed receipt; Pending/Failed is not Saved.
6. After commit, capture again and compare with that exact latest snapshot/receipt
   before displaying **Saved to server: vN · hash**. Newer content blocks that claim.

An uncertain save retains its original command for Retry save. Terminal failed
callbacks retain the last committed bytes. A fresh no-change check can establish
that an undo returned to committed content; new edits use a fresh command.
Local covered-edit counters remain separate from the original receipt generation.

Autosave uses the same capture/callback barrier after a debounced native dirty
event. It never uses native dirty=false as proof of durable application storage.
Application browser draft caching remains disabled. Unsaved typing is lost on
restart; no offline recovery promise is made. This milestone does not claim a
purge of records on other, protected browser origins.

Open/Reopen passes `resumeSnapshotId:null` and the current server working revision.
The backend recovers its latest committed working snapshot automatically. A
submitted working draft is cleared, so the next note starts empty beneath the
existing official history rather than copying the last submitted note.

## Review, official submission and custody

Review captures again while blocked, calls
`POST /api/noting-office/sessions/{s}/previews` with
`{saveRequestId,nativeExportUrl}`, then reads the frozen preview from
`GET /api/noting-office/sessions/{s}/preview`.
Use null Save ID only for the verified version-0 seed. Retained native capture
storage evidence stays server-side and is not in the public preview DTO.

Repeat Review is an authorized frozen read, with no new Save or preview. An
uncertain Preview response can recover that exact frozen record. The officer
confirms its exact canonical text/hash through the existing confirmation endpoint.
Confirmation, submission and Send preserve UUID/payload through uncertain retries.
Submit uses actual working revision, preview ID, canonical hash and controlling
Dak. Note number, author, designation, date and immutable history come from server
responses. Refreshing the official chain does not fabricate records.

Send/Return uses `GET /api/dak/{id}/movement-targets`, not administrator desk APIs.
It lists eligible desk members within real receipt bounds and fetches actual Dak
revision before the initial command. The same payload/revision/UUID is held for
uncertain-response retries. Submit+Send remains the existing atomic transaction.
Digital dispatch retains the physical-original location; physical handover uses
the existing Dak workflow and explicit physical receipt confirmation.

Receive appears only for an actual Pending transfer nominated to the authenticated
officer. Use `POST /api/dak/{dak}/transfers/{transfer}/receive` with a held UUID and
`{expectedRevision,physicalReceiptConfirmed:false}` for the synthetic digital case.
Physical transfers require confirmation in the existing Dak workspace. Editing is
not enabled merely because a recipient can view the file.

## Legal sheet and the red-guide decision

Physical policy remains Legal portrait 215.9 x 355.6 mm; odd left/right 45/15 mm,
even 15/45 mm, top/bottom 25 mm. Native continuous content flows automatically;
submitted notes and signature/marking reservations remain protected.

Option A is implemented: actual native editing with official margins and **no
unverified native red line**, plus an authorized read-only PDF viewer with accurate
alternating red guides and a decorative screen background. Guide coordinates use
the actual PDF.js page viewport/CropBox, verified portrait Legal size and backend
guide metadata. They scale/scroll with the owned page. They are not DOCX shapes or
PDF paint. Clean Print opens the actual authenticated PDF; use Legal portrait,
actual size (100%). Print media CSS also removes the screen decoration.

`?greenSheetId=<published UUID>` opens an existing authorized Legal snapshot without
another render. Its returned Matter ID must match this workspace. The snapshot is
labelled by creation time, and submission clears any previously displayed sheet.
This query does not grant permissions or create an editor route.

Option B is **not implemented**. A version-pinned native page-rectangle bridge
requires experimental proof over zoom, scrolling, odd/even pages, resizing and
device scale. The supported APIs investigated return
[visible page indexes](https://api.onlyoffice.com/docs/office-api/usage-api/document-api/ApiDocument/Methods/GetCurrentVisiblePages)
or [section dimensions in twips](https://api.onlyoffice.com/docs/office-api/usage-api/document-api/ApiSection/Methods/GetPageHeight).
They do not establish a tested screen-rectangle feed for this installed editor.
No guessed CSS offset, undocumented private geometry or printable red object is used.

Letters keep their existing independent document formatting policies and supported
`/matter-drafts/:id` workflow. Uploaded document IDs are not draft IDs and do not
justify inventing `/editor?documentId=...`.

## Remaining boundaries

The host can quiesce its own input; it cannot atomically stop all native tabs before
capture. A frozen exact canonical preview plus deliberate officer confirmation is
the submission authority. Real lease replacement/stale submission was tested.
Simultaneous native edits after the final capture are not claimed to provide a
global editor CAS. Native selected-text remark mapping remains a separate bridge
boundary; existing canonical anchor APIs/history are preserved.

See the [backend safety audit](GREEN_NOTING_V4_BACKEND_SAFETY_AUDIT_2026-10-14.md)
and final acceptance checkpoint for exact test results and evidence.
