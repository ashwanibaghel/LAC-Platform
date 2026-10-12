# Official Green Legal ONLYOFFICE backend acceptance

12 October 2026. Backend source commit:
`33e77d58b94d5fa11c60e23db6ccb2548530c236`.
Evidence/model/conversion commit:
`e06fe51d25ff47471c1bcaf41d0dc1a67f0d25ec`.
Architecture was published first at `f277cf9502273ddc8829077bb7e4935df248b632`.
Worker-disable correction: `b61f6b199799c0cbfceb5a25ed0f75ced29d7621`.

**Result:** real current-note ONLYOFFICE authoring, correlated native callbacks,
immutable snapshots, strict canonical review, two existing atomic numbered official
submissions and continuous authoritative four-page GreenLegalV2 PDF demonstrated.
Anti's user-facing workspace is still a separate integration deliverable. This is
neither a production rollout nor physical printer-driver certification.

## Isolation and running inspection

Approved temporary host: `http://127.0.0.1:5922`.
Browser acceptance client:
`http://onlyoffice-feasibility.localhost:5922/acceptance.html`.
Actual-PDF evidence viewer:
`http://onlyoffice-feasibility.localhost:5922/green-sheet.html`.
Docs remains `http://localhost:8082`, loopback-bound, healthcheck HTTP200 / true.
Actual package/renderer version `9.4.0.129`.

Only the EXISTING synthetic PostgreSQL database on 127.0.0.1:56683 was used:
`lac_onlyoffice_real_9b0459_20261012`. A separate acceptance schema
`lac_office_acceptance_20261012` and uniquely owned regression schemas isolate new
adapter records from the preserved older running API. Regression schemas are retained
for audit; no database/schema deletion. The private INSERT-format baseline was copied
read-only from public and never published. Existing synthetic identities/password
hashes were copied with it; no public/operational accounts were recreated.

The viewer officer initially had only Draft.View/Village.View/Matter.View and was
denied authoring. Six required synthetic grants were then added ONLY in the new
acceptance schema for the two-officer scenario. Original public roles/accounts remain
unchanged. Existing synthetic document storage received append-only new GUID files;
no prior bytes were overwritten/deleted. JWT was read privately from the existing
ONLYOFFICE env file; no values were exposed or copied into Git.

Startup bootstrap and all four background workers were disabled in the new host.
The guard regression checks actual hosted-service registrations in Development and
Testing, rather than treating a configured flag as proof. All running old hosts are
preserved: 5920 PID26664, 5921 PID9864, PostgreSQL56683 PID21596, Docs forward8082
PID19752; protected 5610 PID11440, 5611 PID15020, old PostgreSQL55683 PID10080.
5190/5191 remain unused. Temporary 5922 was restarted only to load corrected binaries
and the verifier pin; final PID22140. No IIS/main/operational database changes.

One script-preparation shell command was rejected by automatic review with only
"blocked by policy". No host started from that command. The explicit prepared
5922 launcher subsequently passed normal approval review; no alternate launch mechanism
or hidden port was used. Exact launcher is retained privately as launch-green-api.ps1.

## Genuine Docs/browser proof

The acceptance client invokes the real cookie login, API, PostgreSQL and DocsAPI;
it has no mocked editor/backend, embedded passwords or persistent browser draft cache.
It is a minimal backend test client, not Anti's production workspace.

1. Officer A typed the synthetic Kanungo note in the real editor. Native signed
   status-6 callback with matching userdata committed DOCX version1, SHA256
   `ef86089ba9c12dc02405f1b12cba7bac3e6b0560e0d0535bdb3c3dccf7a7cfb8`.
2. Preview converted that exact committed DOCX, froze the lease and displayed exact
   canonical text/hash. Officer confirmation updated the existing working note;
   existing Submit inserted immutable official Note1, hash
   `55780241324F5CECE1E9B6D453D12A58ADBF7A53E3C6C5A049FC9C81038FADF4`.
3. Real Dak dispatch/receipt between existing officers advanced revisions4 ->5 ->6.
   Viewer login first returned403 for authoring; after synthetic-only grants and
   confirmed receipt, B opened its own blank editor. A's submitted text was absent.
   The blank note correctly inherited Liberation Serif12 from server DOCX defaults.
4. B authored a four-page current note. Native force-save committed SHA256
   `872b8fbc584d8aa87d7ffd4a63001ce0f102c4edd2aea7217343ebb949158293`.
   Replacing/closing that editor opened a new private key and immutable version0
   recovery snapshot with the SAME hash. Actual editor Find located the saved
   ACCEPTANCE-OFFICER-B-END marker (1/1 on page4).
5. A no-change force-save on the reopened editor returned Failed /
   DocumentServerCommandRejected, with no fabricated saved snapshot. The last
   committed text remained visible and recoverable. A real additional edit then
   produced a fresh correlated native committed snapshot, SHA256
   `ede8ccb778ec04a451349f87061c8e32844227c089adbf70ba7d6cc685b4f89a`.
6. B froze/reviewed that exact snapshot and existing Submit inserted official Note2,
   hash `86004A0E78F64532B1D7362C007A0FA8D91DB636BD3667CC46FA664D52187822`.
   Both notes use immutable Office provenance in the SAME existing numbered chain.
   These were separate official Submit + real Dak transfer operations. Atomic Office
   Submit-and-Send rollback is additionally verified by PostgreSQL service tests;
   do not relabel separate browser actions as one atomic Send proof.

Native callbacks after freeze/editor close were rejected with committed history
retained. Separately, live HTTP denial probes signed with the existing private test
JWT (generated probes, not native Docs saves) verified: Note1 PUT/DELETE404, another
actor's session config404, submitted/revoked callback error1/HTTP200, missing/expired
JWT401 and oversized callback413. Official records compared identical before/after.

Initial HTTP probe failure was a PowerShell client issue: WebRequestSession retained
an Authorization header, so its supposed unsigned request was still signed. The
probe now clears that header explicitly. This was not evidence of an unsigned
callback bypass or a proven hosting-limit failure. Explicit length and streamed-body
bounds were added as hardening and verified through PostgreSQL HTTP and live Kestrel.

## Actual continuous physical PDF

Actual Docs conversion produced a clean four-page PDF, SHA256
`692cf4bbd5e7d47629b502eafb4c3e47889667b5f33a827f166645e246133b36`.
Immutable source, output, renderer/font pins and page count are persisted. Its backend
sheet ID is `617a7fe6-618d-4e5c-ba2f-62604d2c732e`; authorized PDF URL:
`/api/noting-green-sheets/617a7fe6-618d-4e5c-ba2f-62604d2c732e/pdf`.

All pages are 612x1008pt (215.9x355.6mm), portrait. Source XML has one section,
global mirror margins2551/850twips, top/bottom1417twips. Policy is GreenLegalV2;
integer-twip/raster precision allowance is 0.02mm, not a typography-sized clearance.
Note2 begins on PAGE1 at approximately160.561mm from the top, below Note1's unsplit
signature/designation/date/marking reservation. B continues naturally through pages2,
3 and4; final signature reservation remains together on page4. All four rendered
pages were visually inspected. No officer boundary page break or parity restart.

600dpi measured visible-ink clearance:

| Page | Left mm | Right mm | Top mm | Bottom mm |
| --- | --- | --- | --- | --- |
| 1 | 45.000 | 15.748 | 25.993 | 27.813 |
| 2 | 14.986 | 49.445 | 25.950 | 31.327 |
| 3 | 45.000 | 19.431 | 25.950 | 41.571 |
| 4 | 14.986 | 46.948 | 25.993 | 95.885 |

The gate matched EVERY composed printed character in order, allowing only pagination
whitespace differences. All pages have zero nonneutral painted objects and zero image
decorations. No red guide/green background is present in authoritative DOCX/PDF.
The earlier approximately0.76mm font-box concern is not reproduced as printed-ink
intrusion: minimum bottom ink here is27.813mm; font boxes also clear25mm in this file.

The separate evidence viewer uses PNGs rendered FROM THIS exact PDF and live authorized
metadata. Red DOM guides alternate x45mm /170.9mm; decorative green and guides are
screen only. Its print CSS excludes screen pages; the print action opens the clean
authoritative PDF, requiring Legal /100% actual size. Anti must implement the same
overlay separation against the real PDF viewport in its user-facing workspace.

Renderer font manifest SHA256:
`a0b7f848c9af41fc6716cbeaefa0865c5deaf84dbbef847f65441e65dacc43f8`.
Read-only actual container font-binary hashes are retained locally. Runtime/version,
manifest and verifier pins are checked before/after conversion. Immutable deployment
provisioning is still needed; a font-map hash does not attest every mutable TTF byte.
This English synthetic fixture passes. Other scripts/fonts require actual glyph/print
certification; missing glyphs fail the printed-text gate. Actual printer hardware/
driver settings were not exercised and cannot be forced by the backend.

## Regression evidence and repairs

72 distinct relevant .NET tests are passing across the recorded runs, plus six Python
physical/PDF text tests. Six PostgreSQL Office test methods use the existing synthetic
database via isolated schemas; Docs HTTP/authentication fixtures are explicitly mocked
where applicable. Real Docs/browser evidence above is separate.

* office-final-regressions.trx:71 passed /1 failed initially. The failing citation
  fixture had not supplied existing source PDF bytes to its test storage. Corrected
  fixture reads ONLY the existing synthetic baseline document files.
* office-final-postgres-fixed.trx:7 passed, including all six PostgreSQL methods and
  the JWT/origin unit check. Covers concurrent same-key requests, failed save/retry,
  conflict payloads/revisions, login/permission revocation, exact repeated-text
  citations, explicit changed-text reset, immutable remarks and raw SQL/history guards.
* office-callback-body-bound.trx:affected PostgreSQL HTTP regression rerun passed
  after final bounded-callback hardening.
* office-and-legacy-regressions.trx:65 passed, one OLD HTTP test skipped because its
  helper creates a new database. Its new Office replacement runs PostgreSQL HTTP
  actor binding, headers, signed-versus-unsigned callback fields and size protection.
* office-atomic-send-and-render-history.trx:stronger affected test rerun passed with
  typed dispatch rejection/rollback, SUCCESSFUL atomic Office Submit-and-Send,
  idempotent Send replay, committed Green Sheet source/PDF SQL immutability and
  denied retired-lease resurrection. Its physical evidence rows are database-only
  synthetic fixtures, not purported mocked renderer success.
* New converter tests include the actual sanitized Docs9.4 exported DOCX fixture,
  exact Unicode/marks/anchors, unsupported content rejection, deterministic byte
  composition, one global Legal section and unsplit signature reservation.
* Initial converter failures identified separate HyperlinkRelationships, random
  generated relationship IDs, default empty-note metadata, and missing fixed defaults
  for an empty editor paragraph. Each was corrected with focused regression coverage.
* Existing Letter/legacy Office handler regressions and V1 policy assertions pass;
  original public drafts/files were not changed or re-exported for this task.

No new compiler warnings in production code. Three pre-existing xUnit2031 warnings
in AwardExtractionRuleEngineTests remain unrelated. Final callback build passes.
Raw SQL tries cannot update/delete/truncate snapshots, previews, evidence, receipts
or official notes; bad parent/hash/receipt provenance and retired lease resurrection
are guarded. An invalid dispatch rolls back numbered note, Office evidence and lease
submission together.

## Evidence location, screenshots and outstanding ownership

Private local artifact directory is the existing onlyoffice-real-feasibility-9b0459-
20261012 artifact root. Credentials, SQL baseline/account hashes and token-bearing
historical logs remain there and are excluded from publication. Sanitized report,
source and test fixture alone are published. Exact PDF is retained under
output/pdf/official-green-legal-noting.pdf. Useful local files:

* official-green-legal-proof.json; official-officer-page-flow.json; official-render-state.json.
* official-api-denials.json; publication-scan-evidence.json; green-host-launch.json.
* official-office-a-correlated-save-1366x768.jpg; official-office-b-multipage-1366x768.jpg.
* official-office-b-reopen-find-1366x768.jpg; official-office-b-save-failure-1366x768.jpg.
* official-green-screen-guides-exact-1366x768.jpg.

These listed editor/guide captures were dimension-verified as genuine1366x768 browser
images. Other historical captures whose viewport drifted are retained with their
actual dimension report, not claimed as1366x768 despite their original filenames.
No resizing/image fabrication was used.

The supported Colab reconnect was tried once separately: Transport closed. No
global restart/reinstall, credential exposure or substitute MCP process. Host-managed
reconnect remains necessary and does not affect backend acceptance.

Ready for Anti to integrate the implemented API contract and coherent workspace:
read-only continuous PDF beside the current private editor; genuine server save
states; quiesced final save/freeze; exact canonical review; citation re-anchoring;
existing Submit/Send; screen-only guides and clean PDF print action. Backend flags
remain off by default. Production renderer provisioning, broader font certification,
hardware printing and Anti's complete product UX are not represented as finished.
No merge or deployment.

## Shutdown checkpoint

The user needs to take the laptop offline. Official notes, DOCX snapshots, PDF and
test evidence are already durable; no unsaved typing is relied on. Normal shutdown
stops these local processes. After restarting, restore the existing Docker Desktop/
Docs8082 and the existing synthetic PostgreSQL cluster on56683, then run the retained
private launch-green-api.ps1 for5922. That script refuses an occupied5922, reads the
existing JWT file privately, and targets only the synthetic acceptance schema. It
does not initialize/migrate databases or enable workers. Do not initialize a new
cluster, recreate identities or redirect the connection to another environment.
The old5920/5921 hosts use their own preserved launchers; their restoration is separate.
