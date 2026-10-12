# Real ONLYOFFICE acceptance results

Continuation of local checkpoint `9d581966de996e70b5d35d632d25a0b5132f000b`,
12 October 2026, Asia/Calcutta. The running application binary is from source
`9b04590039fe4069680c95f7d6f69be273ad21ea`. This delivery changes only documentation
and read-only PDF acceptance tooling, not the running application or existing environments.

**Result:** genuine LAC-embedded ONLYOFFICE editing, signed save persistence, final
save/key rotation, close/reopen, read-only behavior and clean Legal print-file geometry
have been demonstrated. Official Green Noting acceptance is incomplete: alternating
screen-only red guides and the immutable official-note adapter are not implemented.

## Frontend approval gate and exact launch

The earlier automatic review rejected the frontend launch with only `blocked by policy`.
The user then explicitly asked for the reviewed launch or a user-run fallback. One review
of the same Start-Process mechanism, port and frontend succeeded in this continuation.
There was no alternative process mechanism, hidden port, policy bypass or unrelated host.
No fallback launcher is needed for the now-running frontend.

Executed frontend command (PowerShell):

```powershell
$root='C:\Users\ashwa\.codex\artifacts\onlyoffice-real-feasibility-9b0459-20261012'
if(Get-NetTCPConnection -State Listen -LocalPort 5921 -ErrorAction SilentlyContinue){throw 'Frontend port occupied'}
$p=Start-Process -FilePath 'C:\Program Files\nodejs\node.exe' `
  -ArgumentList ('"'+$root+'\frontend-server.mjs"') `
  -WorkingDirectory $root -WindowStyle Hidden `
  -RedirectStandardOutput ($root+'\logs\frontend.stdout.log') `
  -RedirectStandardError ($root+'\logs\frontend.stderr.log') -PassThru
```

Working directory is the exact `$root` above; process PID 9864. `node --check` passed
before launch; HTTP GET `http://127.0.0.1:5921` returned 200. The server binds only
127.0.0.1:5921, serves the verified actual React build, and proxies API calls only to
127.0.0.1:5920. It contains no PostgreSQL client, operational API address or service
start command. API launch receipt/PID/command line and the PostgreSQL command line
were independently verified against the disposable fixture root and port 56683.

Browser origin: `http://onlyoffice-feasibility.localhost:5921`.
Supported letter route: `/matter-drafts/d6c28346-d52b-40ae-a536-cf1b1ce53cf8`.
Supported Legal route: `/matter-drafts/6e926c6e-ed42-4eee-83b9-b893c4ce7c4d`.
API: `http://127.0.0.1:5920`; Docs: `http://localhost:8082`, healthcheck 200/true.
Disposable database: `lac_onlyoffice_real_9b0459_20261012`, loopback PostgreSQL 17
port 56683. JWT still comes privately from the existing `.env.onlyoffice`; unchanged.
Existing test identities were reused, not recreated. Their passwords remain in the
private local `feasibility-manifest.json`, never in this new report or test scripts.

## Genuine editor, callback and reopen evidence

Using the real LAC login form, the existing editor account opened the actual Docs
iframe. Native textarea/keyboard input inserted `GENUINE-OO-SAVE-20261012-9b0459`.
Native toolbar Save produced a real authenticated callback and changed committed
DOCX bytes, database hash, revision and document version while generation stayed zero.
Closing the only editor client produced the final save and generation rotation.
Reopening visibly retained the marker. Stored XML independently contains it.

The Legal fixture initially had five pages. Native UI confirmed US Legal 8.5×14 inches,
Portrait and Mirror margins: inside 1.77 inches, outside 0.59 inches, top/bottom 0.98
inches (the UI rounds its inch display). Saved XML retains exact twip values: paper
12240×20160, inside/outside 2551/850, top/bottom 1417, gutter 0, mirrorMargins.

Native typing inserted `GENUINE-LEGAL-EDIT-20261012-9b0459`, then ordinary browser
clipboard paste inserted ten synthetic overflow paragraphs. The native editor flowed
to seven pages without manually inserted page breaks. Native Save and final close
committed updated DOCX bytes. Reopen as the viewer found the marker exactly once on
page five of seven through native Find; saved XML includes the marker and paragraph 10.

| Document / stage | Revision / document version | Key generation | Committed SHA256 |
| --- | --- | ---: | --- |
| Letter baseline | 1 / 1 | 0 | 954096222f240904706b58fa7de3270178271dec0df54af84561489cbded4513 |
| Letter native Save | 2 / 2 | 0 | afc64be9de993f840e1377631835d4cccc69297d193da2231253c6485d9476fd |
| Letter final close | 3 / 3 | 1 | fa3131eb657e3923de709c819f2b847a6304d66f9f172bfe29b1ccb4540554ec |
| Legal baseline | 1 / 1 | 0 | 7256e1dedf5fcabc6fe66543af436a93eeaef6ec1daf8982dffd76d646bcee30 |
| Legal native Save | 2 / 2 | 0 | b4bf8cb27d59733e73d835749c6891722b142fcddfbc5c4f2a7e205160d6d5b7 |
| Legal final close | 3 / 3 | 1 | f416232ce709ec0a657671461db3ef77a0d1ad67eebff3a1ab12182305a93e03 |

Each saved byte hash was checked against its committed database hash. UpdatedBy is
ONLYOFFICE. Copies of original and each observed committed DOCX were retained as
test evidence before the legacy service deletes its former mutable pointer. These
test copies are not an implemented immutable production snapshot feature.

Successful callback HTTP requests came from the real Docs host.docker.internal origin
and returned 200. Signed payload bodies were not logged. Status-6/status-2 behavior is
verified through the service's save branches: changed hash/version with generation 0
on native force-save, then generation 1 after final close. No fabricated success callback
or direct database/file write simulated browser saving. A bare HTTP 200 alone was not
treated as proof of a committed save.

## Permissions and denied writes

The editor received edit/true. The viewer logged in through the real UI and received
view/false; only File/View/Plugins/AI and print/navigation controls were present.
Its native textarea had readOnly=true. Normal native key input and typing were attempted;
the letter's revision, generation and byte hash stayed exactly unchanged. A viewer PUT
to the actual draft update endpoint returned 403. Missing and invalid callback JWTs
returned 401/{"error":1}, including after successful real saves. These denial attempts
did not change committed bytes. Unauthenticated config returned 401 in the prior run.

This does not certify actor-bound callback leases, current custody/revocation checks,
stale editor-session protection or transactional official note submission. The existing
download tokens are draft/document-bound; the adapter security gates remain necessary.

## Physical print geometry and corrected bottom-clearance finding

The prior 24.241 mm measurement used PDF text font boxes. Those boxes include font
metrics beyond visible glyph ink. Treating them as printed ink was incorrect. No margin
enlargement, document reflow workaround or visual mask was used to conceal the finding.

The new read-only verifier rasterizes visible ink at 600 DPI and separately inspects
the original color operators. On page four, both PDFium and Poppler measure visible
bottom ink clearance **25.188 mm**. The original five-page export and the browser-edited
seven-page export pass all four required ink-clearance boundaries on every page.
One-twip tolerance (0.02 mm) handles integer DOCX units; it does not allow a 0.76 mm
intrusion. For these actual PDFs, the minimum bottom clearance is 25.188 mm, not below 25.

The native viewer Print button generated a real Docs print PDF and hidden print frame.
The in-app browser did not expose an OS printer dialog. Its media-download helper could
not download the hidden frame. Instead, read-only inspection located the exact generated
synthetic print cache key observed in that frame, and docker cp retained that output PDF.
No browser policy, printer settings or Document Server data was modified by this copy.
No physical printer job was submitted or hardware printing certified.

Native print PDF: `legal-native-print.pdf`, 41,497 bytes, SHA256
`22785856612b694ef7fcd2206401467c29b8f8e9094fc10e62202f886820058e`.
Independent post-save conversion PDF: `legal-v2-after-browser-edit.pdf`, 41,481 bytes,
SHA256 `cb3241132224d23ad90782d24b0a832ede189a71aceb7c42a01787a98e5cf2c7`.
Every native-print page raster is pixel-identical to the corresponding export at the
inspection resolution. All seven pages are 612×1008 pt, 215.9×355.6 mm. Native print
also passes the 600 DPI geometry gate, zero non-neutral paint objects and zero images.

Odd-page left ink starts at approximately 45.00–45.09 mm; even approximately 15.03–15.07
mm. All right ink bounds stay within the complementary 15/45 mm requirements. Minimum
top ink clearance is approximately 25.95 mm. DOCX declared margins remain the exact
25/25 mm profile to twip precision; ink naturally starts inside those boundaries.
Page one retains A's text, an unbroken reserved handwritten signature/identification/
date/forwarding area, then B's text on that same page. Pages 1–4 render identically to
the previously inspected baseline; changed pages 5–7 were rendered and visually inspected.
No clipping, red guides or green backgrounds appear in either PDF.

**Unmet screen requirement:** there are no alternating red screen-only margin guides
in the current editor. Native mirror-margin support does not supply this feature. Future
continuous server preview must overlay odd x=45 mm / even x=170.9 mm guides outside the
authoritative clean PDF; no guide should enter the DOCX or print bytes.

## Reproducible evidence and regression checks

Artifact root: `C:/Users/ashwa/.codex/artifacts/onlyoffice-real-feasibility-9b0459-20261012`.
All final screenshots below have verified encoded dimensions 1366×768:

* `editor-letter-reopen-verified-1366x768.jpg` — saved marker after close/reopen.
* `editor-view-only-verified-1366x768.jpg` — real read-only controls.
* `editor-legal-margins-verified-1366x768.jpg` — native mirror settings.
* `editor-legal-size-1366x768.jpg` — native US Legal dimensions.
* `editor-legal-seven-page-edit-1366x768.jpg` — actual overflow while editing.
* `editor-legal-reopen-marker-1366x768.jpg` — native search finds committed marker.
* `editor-legal-final-continuation-1366x768.jpg` — A/reservation/B on page one.

Additional receipts: `native-save-first-observation.json`, `native-final-save-observation.json`,
`legal-native-save.json`, `legal-final-save.json`, `after-viewer-typing.json`,
`final-committed-state.json`, `viewer-denied-update.json`, `callback-denials-after-real-edit.json`,
`callback-http-evidence.txt`, `legal-original-ink-geometry.json`, `legal-edited-ink-geometry.json`,
`legal-native-print-geometry.json`, `poppler-crosscheck.json`, retained saved DOCX and PNGs.
No JWT, cookies, signed query strings or secrets occur in these summarized receipts.

Run the five meaningful regression cases (parity, intrusion rejection, font-box distinction,
Legal size/twip rounding, and red/green/unknown color rejection):

```powershell
python -m unittest discover -s scripts/acceptance -p test_green_legal_pdf.py
python scripts/acceptance/verify_green_legal_pdf.py <actual-ONLYOFFICE-print.pdf> --output <evidence.json>
```

Both commands were run with the bundled Python runtime (pdfplumber, pypdfium2).
Five tests passed. Original, edited and native-print PDFs each passed the real verifier.
The verifier fails closed on unverified image colors and is a fixture acceptance tool,
not a complete document parser or production print validator.

## Next milestone and unresolved issues

We can begin the backend Office-to-official-note adapter milestone. These tests establish
the genuine Office editing/save/print foundation, not completion of official Green Noting.
Required order: durable correlated force-save receipt/barrier; actor/session leases and
live revision/custody/permission checks; immutable content-addressed snapshot freeze;
strict versioned canonical converter and explicit unsupported-structure errors; officer
confirmation; integration through the existing transactional official note/send chain.
Then build the continuous sheet composer with global parity and reserved signature space,
and a separate screen-only guide layer. If immutable-prefix editing cannot be guaranteed,
author only the current note in ONLYOFFICE beside that one authoritative continuous preview.

Native "All changes saved" describes Office's state, not a durable LAC save receipt.
The host has no correlated save-status UI yet. Persistent browser draft caching remains
disabled; no offline unsaved typing survives restart guarantee is made.

Additional startup finding: BackgroundWorkers:Enabled=false does not suppress
DhcHistoryIntelligenceBridge in Program.cs; it is gated only by the Testing environment.
Its observed polling was confined to this empty synthetic DHC database; read-only SQL
confirmed zero DhcAssistedSyncRuns and zero DhcAssistedSyncItems. Do not represent
the configuration flag as proving that every worker was disabled. Add that missing guard
and a registration regression in the next isolation-hardening change; no operational
database, account or service was touched during this acceptance run.

The supported Colab reconnect attempt still returned Transport closed. No host-owned
MCP control/reload capability is exposed here. No detached substitute, global server
restart, plugin reinstall or credential disclosure was attempted. Host-managed reconnect
remains necessary.

GitHub read-only synchronization check: `git ls-remote` reported remote
`codex/dak-matter-foundation-20261010` at `9b04590039fe4069680c95f7d6f69be273ad21ea`;
documentation commit 9d581966 was local only. It included local synthetic passwords in
its historical version; the current report removes their repetition, but publishing
that ancestor would still publish them. No push or history rewrite was performed.
Future remote synchronization must use a reviewed sanitized publication without
exposing the existing test credentials; preserve the requested original checkpoint.

Final protected listener PIDs remain API 5610=11440, frontend 5611=15020, old pilot
PostgreSQL 55683=10080. 5190/5191 remain unused. Isolated API=26664, frontend=9864,
PostgreSQL 56683=21596, Docs forward=19752 remain running. Main/IIS, operational
accounts/databases, Docker volumes and acceptance environments were preserved. No merge
or deployment. Latest free C/D/E: 5,333,004,288 / 244,867,072 / 82,907,136 bytes; storage
headroom remains limited. The real frontend and read-only Legal tab are left available.

Regression-tool commit: `72f76d1e71a7b4c845044ef19421119ea93a9eae`.
The acceptance documentation follows in a separate local reviewable commit. Neither
changes the baseline binary used in the real browser test.
