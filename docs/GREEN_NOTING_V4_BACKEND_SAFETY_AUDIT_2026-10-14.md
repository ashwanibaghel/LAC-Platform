# V4 backend safety gate

Reviewed the existing V2 architecture against base
`5bbd843dc4e5de9fc0d38bf8451b6072d8b31a97`, independently of the proposed React
route. This is an implementation audit by the same agent, not an external security
sign-off. No architecture replacement, operational migration, merge or deployment.

## Results

- Manifest seal binds actor/session/Matter/hash. Live authority checks ordered
  official note IDs, numbers, versions, hashes, metadata and current officer identity.
- Submitted SDTs must retain order, tags, locks, metadata, exact text and semantic
  marks. Current-note-only conversion never searches for the first matching quote.
  Canonical offsets remain UTF-16, with paragraph boundaries and repeated anchors.
- Tightened inherited paragraph styles, SDT attributes/IDs and duplicate/cyclic
  package relationship traversal. All fail closed before canonical import.
- Committed callbacks require the exact native captured text AND marks, correlated
  request identity/generation, authorized lease and immutable hashed snapshot.
- Submit/Send use existing Dak then Matter transactional locks and immutable note,
  preview and snapshot records. Wrong hashes roll back. Source/PDF reads recheck
  authorization; signed source tokens bind their intended purpose and resource.
- New no-change checks revalidate revision, live chain, active identity, RBAC/custody,
  lease and captured bytes. Reopen seeds carry no fabricated committed receipt.
  Fresh Review capture is retained and revalidated during submission.
- Installer recovery removed only the explicitly approved unused extraction.
  Observed recovery: 1,521,225,728 bytes. No uv prune or Colab interruption occurred.

## Validation

Private receipts are under
`C:\Users\ashwa\.codex\artifacts\green-noting-v4-envelope-20261013\test-results`:

| Receipt | Result |
| --- | --- |
| v4-pre-ui-safety-audit-20261014.trx | 67 passed, zero skipped |
| v4-package-graph-20261014.trx | 39 passed, zero skipped |
| v4-native-A-safety-20261014.trx | real native A fixture passed |
| v4-native-B-safety-20261014.trx | real native B fixture passed |
| native synchronization + save-barrier Node tests | 31 passed |

The PostgreSQL suite uses explicitly named disposable test schemas in the existing
synthetic database at 127.0.0.1:56683. Protected databases and previous evidence are
preserved. Only sequential incremental compilation was used on limited capacity.

## Boundaries still requiring browser acceptance

Docs' dirty event is boolean, not a transactional global editor sequence. Local
input quiescing cannot prevent another native tab from editing after capture.
Review must capture again, freeze the lease, and require officer confirmation of
the exact frozen canonical text. No claim of a global native editing CAS is made.

New no-change endpoints have PostgreSQL coverage but still require real Docs/browser
Save, repeated Review, reopen and native callback checks in the final React route.
Network-loss retry must preserve UUID and payload; changing text must start a new
command. No offline unsaved typing persistence is promised.

The static submit validator relies on PostgreSQL immutability of the previously
authenticated manifest and captured evidence; it is not a second JWT signer.
Package strictness is tested against both retained actual native fixtures, not all
possible ONLYOFFICE versions, fonts, layouts or plugins.

Exact native screen page rectangles remain unproven. Supported document APIs
provide page indexes or physical twips, not verified DOCX screen rectangles.
Option A can retain true native margins and show accurate guides on an owned
read-only PDF. Option B requires a proven version-pinned native geometry bridge;
it is not implemented. No guessed native CSS line or printable red shape is added.

Final UI approval and fresh authenticated A/B workflow acceptance remain pending.
