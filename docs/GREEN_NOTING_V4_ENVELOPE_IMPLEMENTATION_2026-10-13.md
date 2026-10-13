# Continuous native Green Noting: implementation and real acceptance checkpoint

**STOP for independent review. This is a verified backend/transport checkpoint,
not accepted V4 functional UI and not a deployment.**

Branch: `codex/green-noting-eoffice-workspace-v4`.
Base: `4b4254b4369f9402a9597fe8b288e41499f90f8e`.
Contract commit: `c8b02127f5e38efd995ffacee407ab9944a710c7`.
Backend/security/print commit: `83d8a898d1ad4ecfa2d322f157d155c51d75f4aa`.
Native bridge/client types commit: `afb887ff6b56ee3a9880e44b31faa2234208a7f0`.
The documentation/evidence commit containing this file follows those commits; use
the reported pushed HEAD for the complete checkpoint. No merge or deployment occurred.

## Gate results

| Gate | Status | Exact scope |
| --- | --- | --- |
| Additive sealed ContinuousGreenEnvelopeV2 | PASS | Server manifest/history validation, immutable PG evidence and current-only canonical conversion; V1 remains default |
| Immediate final native keystroke -> frozen canonical | PASS | Genuine authenticated desktop Docs 9.4.0.129 runs, ASCII final markers, quiesced capture plus correlated matching bytes; not universal IME/browser certification |
| Delayed callback / rapid edits / exact save replay | PASS | Real native callback delay, Pending separate from Committed, same UUID returns same snapshot |
| Invalid JWT callback / failed request | PASS | Real callback JWT corruption gave 401; no snapshot; recovery excludes failed marker |
| Real lease expiry | PASS | Natural 3-minute lease expiry; subsequent native capture/save rejected 409, no new snapshot or official note |
| Real conflicting native tab | PASS | Two genuine editors sharing one lease; different captured/callback text hashes; real callback rejected before 60-second deadline |
| Strict physical two-page official PDF | PASS | Actual GreenSheetService + Docs conversion + unchanged 600-dpi verifier + both page images inspected |
| Five-page native print benchmark | PASS | Odd/even Legal geometry, plain/bold/italic, digits, representative synthetic text; all five page images inspected |
| A -> official #1/Send -> B actual Receive -> immutable #1/current #2 -> official #2 | PASS | Real authenticated API, isolated PG schema, actual transactions and distinct official identities |
| Actual original source remains separately authorized | PASS | Actual API response SHA256 independently matches original fixture/storage identity |
| Accurate alternating screen-only native red guides | BLOCKED | No supported page-rectangle/event bridge verified on the installed native editor; no guessed overlay implemented |
| Fresh no-change force-save UUID | FAIL / limitation | Native command error 4 was reproduced; exact committed UUID replay passes; final parent policy still required |
| Final V4 route, broad UI reconstruction, owner functional acceptance | BLOCKED | Intentionally not implemented before independent review and guide decision |

## Implemented authority and persistence

The existing note chain remains authoritative. Under Dak -> Matter transaction locks,
V2 captures exact ordered official note IDs, numbers, version, canonical content/hash,
author/designation/desk/time, source Dak and citation/document metadata. The immutable
lease stores its manifest/hash and HS256 seal bound to lease/actor/Matter. A live-chain
change fails closed. V1 default and original Open/Save idempotency fingerprints have
regression coverage and remain compatible.

One native DOCX has a single mirrored Legal section, one locked control for each
historical note, a locked unsubmitted context and exactly one editable current control.
Historical text/marks/order/tags/locks/metadata/signature spacing are validated against
server records. Extra/nested controls, external/data relationships, fields, changed
layout, unsupported formatting and unexpected package parts are rejected. The sole
native empty bibliography normalization uses a bounded DTD-disabled XML reader;
entity expansion, namespace lookalikes, inherited theme colors and paragraph-mark
formatting attacks have fail-closed regressions. Neutral native 12 pt paragraph marks
are allowed after the real paste test exposed that normalization requirement.

V2 Save stores immutable downloaded native capture bytes/hash/size plus canonical
text and semantic formatting hashes before requesting force-save. Native export URL
origin and cache path are bound to the actual document key. A callback can commit
only its correlated target with identical text AND marks. Full envelope and retained
capture integrity are checked again before preview and transactional submission.
Recovery extracts only validated current content and composes it against freshly
captured live history. No native history becomes duplicated current-note text.

The supported lifecycle uses [downloadAs/onDownloadAs](https://api.onlyoffice.com/docs/docs-api/usage-api/methods)
and [document-state events](https://api.onlyoffice.com/docs/docs-api/usage-api/config/events).
Export permission is enabled only for authorized V2 leases; V1 retains its existing
download policy. The React frame exposes the production synchronization bridge and
the client has additive typed arguments, but the final parent workspace has not
been reconstructed or registered. See [the exact API handoff](GREEN_NOTING_V4_ENVELOPE_CONTRACT_2026-10-13.md).

## Genuine authenticated proof

This experiment runs the complete real LAC API with a deliberately thin authenticated
diagnostic client, visibly labelled **V4 UI NOT ACCEPTED**. It is not a completed
React workspace, mocked API, textarea editor or timer-confirmed save. The left
iframe is actual ONLYOFFICE; the independent right PDF.js viewer fetches authorized
source bytes. The callback relay forwards native bodies/JWTs to the real API unchanged
except explicitly recorded delay/corrupt-signature negative tests. Its logs alone
are not the authority: PostgreSQL records, retained bytes, strict converter output
and frozen canonical/official records were independently compared.

Fresh synthetic Matter: `2a7affc3-4885-446d-8d1a-ffeea85d8ed2`.
Dak: `d9c1133a-907b-430d-ae86-8b6b17072faa`.
Existing synthetic test identities were cloned into the fresh schema; operational
identities/accounts were neither recreated nor modified. Fixture-only permissions
were granted in that schema. Before B's actual Receive, opening the working lease
returned 403. Original custody was created through real Register/Mark/Receive/Classify
transitions; fabricated confirmed assignment was rejected by PG during preparation.

| Official note | Immutable identity | Canonical SHA256 |
| --- | --- | --- |
| #1, Kanungo | `7bb7f1c7-9612-4d9c-b4ac-9ef078966f19` | `888651C1008E03C7AD755796AFADB4B068851B789256C027243D41C9538BC0F4` |
| #2, Tehsildar | `a0352bfe-58cc-4472-ba63-cb5cf00afec3` | `075ECE3427F9FA3C83A9901DFC8E3778A55B9042286845397363A1D0B70998FB` |

A typed final marker `A-FINAL-NATIVE-20261013.`, immediately clicked Review, then
confirmed the actual frozen preview and atomically submitted/sent #1. B actually
received the Dak, saw native locked #1 above the current note on the same page, saved
`B-SAVE-REOPEN-20261013.`, and reopened with a new lease/document key from committed
bytes. Exact save UUID replay returned identical evidence. A deliberate edit into
history did not alter official #1. Invalid-JWT callback typing was not committed;
recovery excluded `B-FAILED-CALLBACK-UNCOMMITTED-20261013.`.

B's real native ten-paragraph continuation flowed to two pages. Its immediate final
marker `B-FINAL-NATIVE-20261013.` appears identically in actual committed DOCX,
frozen canonical and submitted #2. Snapshot #1 for that lease is
`58c4f920-3e82-4e8c-af2a-15dc3b3991a2`, DOCX SHA256
`10bed983e6938811de0bc8752e888c88843994ce228ae2b51a7378aa9601997a`.
Full A/B native bytes and captured synthetic manifests are published below and pass
the current strict converter offline as well as the real API submission checks.
The final chain remains exactly two official notes after all negative tests.

The real conflicting-tab retest used request `666f5e40-60db-44aa-82e5-611c0371e6f8`.
First captured current hash:
`4BA12DBBDE19B42FD42A6706839DF764311CBCDA901D654D24BBC46BCD3DE08C`.
Newer native callback current hash:
`B9BC3D2F5D4A57167325CEF704BF773710FE9BC124490D0F93293C4D6D5745A2`.
Both complete envelopes independently pass the current strict converter. The real
API responded `200 {error:1}` at 22:32:49 IST, before the 22:33:44 IST deadline.
No snapshot was committed; latest committed version remained 0. UI eventually
reported Failed/Timeout. Stored Pending is retained by the existing lazy timeout
projection until subsequent save/recovery transitions; it is never reported Committed.

## Physical print evidence

Legal portrait is exactly 612 x 1008 pt (215.9 x 355.6 mm). Official XML margins are
unchanged 2551/850 twips mirrored and 1417 twips top/bottom. Composer V2.2 adds
57 twips (1.0054 mm) conservative paragraph inset inside each official horizontal
margin, with explicit tab/compatibility policy. This resolves the observed 44.958 mm
ink intrusion without reducing 45 mm, changing paper size or weakening the verifier.
Canonical notes are unchanged. No page break is inserted at the officer boundary.

Actual official PDF ID `d08a903f-8350-4b6c-a98c-2dd55a9ff14f` has two pages and SHA256
`22ed9699df670f540e5c5223752b9e4d3864993c7dc0261b7d908222184ed3d9`.
The authorized browser response independently has this same hash. Composed DOCX SHA:
`0253ea32cdcdcaf4b65fe845e18ce10281c5251377c2f1a5f382d690097d13e3`.
Renderer: `9.4.0.129`; AllFonts manifest SHA:
`a0b7f848c9af41fc6716cbeaefa0865c5deaf84dbbef847f65441e65dacc43f8`.

| Actual official page | Left/right visible ink clearance mm | Top/bottom mm |
| --- | --- | --- |
| 1 (odd) | 46.016 / 16.679 | 25.993 / 45.127 |
| 2 (even) | 16.002 / 46.694 | 25.950 / 259.800 |

The separate five-page native benchmark SHA is
`15da917b0651be6e7adedc3a32245d6c809cc04ca8406d290b9dcac348919178`.
Odd left clearance is 45.974 mm; even left 15.960 mm; minimum top 25.950 mm,
minimum bottom 28.025 mm. Ordered text coverage passes. Both official pages and
all five benchmark pages contain zero images and zero non-neutral paint objects.
All seven page renders were visually inspected. This certifies these pinned English,
digits, approved marks and included Unicode fixtures; additional scripts/fonts need
their own print certification. Printer scaling must remain actual size.

Original authorized source SHA256:
`22e003f244bfa3836f5f3636fdd2e3b46c5e091b595308b31b8f1213a5d6f7e5`.
Its separate original `contentUrl` remains usable after both submissions. Browser
SHA256 of the actual authenticated API bytes independently equals this hash.

## Remaining limitations and honest failures

No supported native page-rectangle/event bridge was verified. Installed plugin API
current-page integers and [visible-page indexes](https://api.onlyoffice.com/docs/office-api/usage-api/document-api/ApiDocument/Methods/GetCurrentVisiblePages/)
do not establish screen rectangles over scroll/zoom/resize. No hardcoded overlay,
undocumented editor global, printable red shape or background was used. Native
red-guide alignment tests are BLOCKED, not passed. This is an evidence limit, not
a claim of universal vendor impossibility. Owner review must choose an accurate
screen-only PDF.js review guide or a supported version-pinned native geometry bridge.

The first bulk Unicode automation sequence produced only `Same` plus the emoji,
without its intended ASCII suffix. The frozen preview exposed the missing suffix
and was not submitted. Root cause at that automation/input boundary is unresolved;
separate actual ASCII typing plus a final individual keystroke passed. Do not claim
all IME/bulk input behavior is certified. The first native paste save was correctly
rejected for paragraph-mark formatting; the narrow neutral 12 pt policy fixed it,
while bold/larger-font/theme attacks remain rejected.

The first concurrency run also exceeded its original 12-second timeout and cannot
identify a single rejection cause. It is preserved as inconclusive; the successful
60-second retest above independently proves text mismatch before timeout. An offline
B evidence-export helper initially changed UTC timestamp presentation via PowerShell
JSON date conversion and failed historical comparison. Preserving raw manifest JSON
timestamps corrected the fixture; the real server manifest/notes were never changed.

Persistent browser draft caching remains disabled. Native live-cache recovery during
an API-only restart is not committed persistence and is not an offline/reboot promise.
Only the retained server snapshot and exact canonical records establish durability.
No legacy draft text was imported by this client; no claim of a fresh all-profile
legacy-cache purge is made in this milestone. Native selected-text remark offset
mapping and the final shared React workspace remain separate review-gated work.

## Tests, evidence and reproducibility

Final focused backend run: **61 passed, 0 failed, 1 skipped** (explicit synthetic
preparation intentionally not run again). Real PostgreSQL security coverage includes
tamper/immutability, changed chain/recovery, expiry, idempotency and concurrent UUID,
revocation/custody/RBAC, signed callback actor/body binding and repeated-text anchors.
Their native HTTP transport is controlled by unit fixtures; the separate browser
and retained native bytes provide actual Docs evidence. Four further explicit native
fixture executions validated actual submitted A/B and conflicting capture/callback.
They are executions of one reusable validator test, not four additional unique tests.

Final frontend Node run: **27 passed, 0 failed**. Final app TypeScript check:
`tsc -p tsconfig.app.json --noEmit` passed. Full TypeScript/Vite build passed earlier
in this milestone before the final optional client arguments/callback cleanup; final
type check and focused tests cover those later changes. Three existing xUnit2031
warnings and the earlier Vite large-chunk warning remain; no warning-free claim.

Commands and sanitized case names/results are in
[test-results.json](receipts/green-noting-v4/envelope2/test-results.json).
Private PostgreSQL tests require explicitly supplied synthetic loopback connection
and private INSERT baseline; they clone random schemas and never drop databases or
write public. Native fixtures require `LAC_ENVELOPE_NATIVE_ROOT` under private task
artifacts and optional `LAC_ENVELOPE_EXPECTED_MARKER`. Source fixture has actual Docs
9.4 bytes; no mock is substituted when native output is unavailable.

- [Official identities, actual Send/Receive and immutable evidence](receipts/green-noting-v4/envelope2/official-workflow.json)
- [Actual native save receipts](receipts/green-noting-v4/envelope2/native-save-receipts.json)
- [Actual conflicting native callback](receipts/green-noting-v4/envelope2/concurrent-native-rejection.json)
- [Sanitized actual callback chronology](receipts/green-noting-v4/envelope2/sanitized-real-callback-events.json)
- [Official two-page PDF](receipts/green-noting-v4/envelope2/actual-official-two-note-green-sheet.pdf)
- [Official physical/text/color verification](receipts/green-noting-v4/envelope2/actual-official-two-note-geometry.json)
- [Five-page native PDF](receipts/green-noting-v4/envelope2/native-five-page-geometry-fixture.pdf)
- [Five-page physical/text/color verification](receipts/green-noting-v4/envelope2/native-five-page-geometry.json)
- [Original source PDF](receipts/green-noting-v4/envelope2/authorized-original-synthetic-receipt.pdf)
- [Artifact hashes](receipts/green-noting-v4/envelope2/artifact-hashes.json)
- [Exact changed files from the verified base](receipts/green-noting-v4/envelope2/changed-files.json)
- [Authenticated API PDF response hashes](receipts/green-noting-v4/envelope2/authenticated-api-pdf-response-hashes.json)

Actual screenshots are complete 1366 x 768 viewport captures, dimensions checked;
no image resizing, retouching or simulated save state:

- [A frozen final text](receipts/green-noting-v4/envelope2/A-frozen-final-text-1366x768.jpg)
- [A actual official Send](receipts/green-noting-v4/envelope2/A-official-send-1366x768.jpg)
- [B immutable #1 and independent source](receipts/green-noting-v4/envelope2/B-immutable-note1-and-source-1366x768.jpg)
- [B real native two-page continuation](receipts/green-noting-v4/envelope2/B-native-two-page-and-source-1366x768.jpg)
- [B exact frozen canonical](receipts/green-noting-v4/envelope2/B-frozen-final-text-1366x768.jpg)
- [Real invalid-JWT failure](receipts/green-noting-v4/envelope2/B-invalid-JWT-save-failed-1366x768.jpg)
- [Real conflicting-tab rejection](receipts/green-noting-v4/envelope2/concurrent-native-edit-rejected-1366x768.jpg)
- [Natural expired-lease rejection](receipts/green-noting-v4/envelope2/expired-native-save-denied-1366x768.jpg)
- [Actual official page 1](receipts/green-noting-v4/envelope2/official-pdf-page1-1366x768.jpg)
- [Actual official page 2](receipts/green-noting-v4/envelope2/official-pdf-page2-1366x768.jpg)
- [Preserved native #1/#2 and original source](receipts/green-noting-v4/envelope2/preserved-native-official-chain-source-1366x768.jpg)

## Isolated environment and next milestone

Existing ONLYOFFICE `http://localhost:8082` is bound `127.0.0.1:8082:80`, health 200
`true`, existing container reused. After the owner's laptop reboot, normal disposable
PG crash recovery and hidden launch of installed Docker restored availability. No
reset/reinstall/prune/WSL unregister, socket/volume deletion, reboot or pagefile change
was performed. Old probes/checkpoints/files remain preserved; their diagnostic hosts
5938/5939 were not relaunched after reboot to conserve resources.

Real isolated API: `http://127.0.0.1:5940`.
Diagnostic browser client: `http://lac-v4-isolated.localhost:5940` (cookie origin
separate from protected localhost services). Transparent callback relay: loopback
5942, file/callback allowlist only, no DB connection. Synthetic PostgreSQL:
`127.0.0.1:56683`, database `lac_onlyoffice_real_9b0459_20261012`, fresh schema
`lac_office_test_93b2807ee5504e3f948e60bc8ca94634`. Bootstrap/workers disabled;
task-owned contentRoot/storage only. Existing public/acceptance schemas are preserved.
JWT and test passwords remain private, with unchanged original configuration file.

C: free space fell to approximately 0.6 GiB as Windows automatically grew the pagefile
and retained test schemas during this run. D:/E: remain critically low. No files were
deleted to manufacture headroom. Further heavy builds/new fixture schemas should
wait for approved safe recovery; retained audit data must not be pruned. This does
not invalidate the completed byte/geometry tests but limits further unattended work.

Protected main/IIS/shared wwwroot, operational DB/accounts, worktrees and services
5190/5191/5610/5611 were not modified. No service was launched on 5920/5921/5922.
The 5940/5942 disposable host remains available for inspection; leases expire normally.

Independent review should first examine sealed authority, closed DOCX normalization,
native capture/callback correspondence, DB immutability/rollback guards and bounded
renderer policy. After review and the owner's red-guide decision, wire the single
native V2 bridge into the final shared workspace, preserve separate authorized source
viewing, implement no-change receipt handling and explicit conflict/recovery states,
and run owner acceptance. Native historical remark offset mapping and further
script/font/input coverage need their own exact tests. Do not create a second official
noting system or claim full V4 acceptance at this checkpoint.
