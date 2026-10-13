# Native V4 no-change and review contract

This is an additive contract over verified base
`5bbd843dc4e5de9fc0d38bf8451b6072d8b31a97`. Existing V1 Save behavior and stable
idempotent failed-save retry are preserved. The focused backend safety suite passed
67 tests, package/envelope validation passed 39 tests after the final graph guard,
and both previously captured real 9.4.0.129 native DOCX fixtures passed. Frontend
barrier/native bridge tests passed 31 tests. Browser acceptance remains separate;
this contract does not permit a production deployment.

Before each V2 Save or Review, block local editor input and use the supported native
bridge to export/synchronize actual DOCX. A modified/not-modified event or a cached
client receipt alone cannot establish that there are no new changes.

`POST /api/noting-office/sessions/{s}/save-check` requires authentication,
UUID `Idempotency-Key`, current quoted `If-Match`, and `{nativeExportUrl}`.
It validates the authorized document-key-bound native export, full historical
envelope and live chain under existing Dak -> Matter locks, then checks semantic
current text AND marks against the latest hash-verified immutable server snapshot.
It creates no Office save request, snapshot, committed receipt or force-save command.
The normal audit/idempotency command record remains distinct from a save receipt.

Response: `{noChanges,snapshotId,sha256,snapshotVersion,sessionRevision,committedReceipt}`.

- `noChanges:false`: request a normal correlated Save with the same captured native
  export, then await the actual committed receipt. Native error 4 remains a failure.
- `noChanges:true` with a committed receipt: the server revalidates lease, actor,
  permissions/custody, revision, live history, current snapshot bytes and original
  native capture evidence. Return that same receipt ID/generation/snapshot/version.
  Display **No new changes to save.** Do not fabricate a new commit/generation.
- `noChanges:true` with no receipt and snapshot version 0: this is the existing
  server-created/recovered seed for the new authorized lease. Display **No new
  changes to save. Server snapshot restored.** It is not a new native committed save
  and no receipt from a revoked previous lease is reused.

Changed identity metadata, scope/custody, official chain, session revision, latest
snapshot or expiry invalidates reuse. Live pending Save blocks the check. A replay
also rechecks live revision/snapshot and fresh downloaded capture correspondence;
cached command results cannot describe superseded content as current.

For Review, the parent remains blocked, using the returned current revision and
original committed receipt ID (or null for the unchanged server seed). Call the
existing `POST /sessions/{s}/previews` with `{saveRequestId,nativeExportUrl}`.
The additive nullable native field is omitted by legacy callers. Normal correlated
preview still works unchanged. A seed preview requires a fresh exact native match.
Native capture is checked again before freezing to reject an edit between check
and Review. Its immutable byte/hash/semantic evidence is retained on the preview.

`GET /sessions/{s}/preview` revalidates a Frozen/Reviewed lease and returns
`{session,preview}` for repeat Review. It creates no second preview, noting file or
save. Reviewed confirmation and transactional Submit remain the existing endpoints.
Submission revalidates native preview capture, snapshot bytes and current-only
canonical content. An unchanged seed can be submitted only with retained fresh
native review evidence; it is not relabelled as a committed callback snapshot.

After failed callbacks, different current content must not reuse the old committed
receipt even if Docs considers itself clean. Undo back to exactly the committed
content can reuse that valid original receipt. New edits use a fresh command; an
explicit unchanged retry retains the original Save UUID/payload/revision. Reopening
extracts only committed current content and composes fresh immutable history.

The UI must separately track the local capture it has covered and the original
receipt's generation. It must not change receipt identity/generation to fit a newer
local counter. A newer dirty transition after capture prevents a Saved/no-change
claim and restarts Review. Frozen review destroys editing input; repeat Review is
an authorized read, not a new force-save.
