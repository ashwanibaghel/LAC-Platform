# ONLYOFFICE real integration feasibility checkpoint

Status: **BLOCKED at environment recovery; real editor acceptance not executed.**
Starting revision: `ec2bbee0d0057a0ab45ff4b83f3ed91a8e09bf94`.
Isolated checkout: `E:/LAC-Dak-Matter-Foundation-20261010`.
Branch: `codex/dak-matter-foundation-20261010`.
Date/time convention: 12 October 2026, Asia/Calcutta.

The user explicitly authorized starting the existing Docker engine and isolated Docs service.
No factory reset, reinstall, pruning, volume deletion, WSL reset/unregister, reboot,
unrelated-container changes, or system-wide repair was authorized. This checkpoint does
not replace the previously published functional audit or claim its deferred Office adapter exists.

## Verified environment and recovery evidence

* `C:/LAC-Platform-onlyoffice` exists, clean, at
  `70332ec8e63cb1d32e5a06e66033784605125fcb` during discovery.
* Its `docker-compose.onlyoffice.yml` and `.env.onlyoffice` exist. The environment file
  contains one `ONLYOFFICE_JWT_SECRET` entry meeting the minimum secret length; its
  value was never printed, copied into this report, or committed.
* Compose specifies `onlyoffice/documentserver:9.4`, loopback `127.0.0.1:8082:80`,
  JWT enabled, development private-address callbacks and four named persistent volumes.
  Neither Compose nor JWT configuration was changed. Container JWT equality is unverified.
* The isolated backend checkout and the calling checkout also contain Compose and the
  example environment file, but neither contains an actual `.env.onlyoffice`.
* Docker Desktop `4.91.0.239619`, CLI `29.8.0`, context `desktop-linux` and WSL
  `3.0.1.0` are installed. Ubuntu and docker-desktop are registered as WSL2 distributions.
* There is no installed `com.docker.service` Windows service. This is not by itself a
  failure: the installed per-user WSL2 configuration can operate without that privileged
  helper. No service was installed and no service start mode was changed.
* First launch of the existing Desktop executable through the agent shell failed before
  engine initialization. Docker reported inability to rename
  `C:/Users/ashwa/AppData/Local/Docker/run/sailor-ingest.sock` to `.stale`;
  Windows returned error 1920, "The file cannot be accessed by the system."
  Read-only `fsutil reparsepoint query` returned the same error. No sockets were deleted.
* Launch through Windows Explorer got past that error. Docker started WSL and the
  previously stopped `vmcompute` service became Running. Direct `Start-Service vmcompute`
  was denied to the agent, but Docker/WSL started it normally; no elevation or ACL changes.
* That attempt subsequently failed in WSL bootstrap with `0xc00000fd` and a broken
  socket-forwarder/VM handshake. Windows Application events 1000 and 1001 confirm
  `wslservice.exe` version `3.0.1.0` faulted with `0xc00000fd` at approximately 00:15:48 IST.
* After Docker had reported failure and Quit, the remaining failed Docker launcher process
  was stopped by its verified PID/executable path. No WSL shutdown, process-wide kill,
  unrelated program stop, or container stop command was issued.
* One bounded WMI process launch of the same installed Desktop executable succeeded
  (Create ReturnValue 0, launcher PID 1948). Its parent was verified as WmiPrvSE, avoiding
  inherited packaged-shell launch context. No Windows configuration was altered.
* During that launch Docker briefly reached the engine. At 00:18:25 IST its own log
  rediscovered existing container `ebdae7577ae6621e270c871ceeefc307d8f9b0994d907a8e1c3791c08eabea8b`
  with a forward from `127.0.0.1:8082` to `172.18.0.2:80`. This is log evidence of an
  existing port-mapped container, **not** a successful container inspection, compatible
  image/JWT verification, healthy Document Server or browser acceptance.
* The same startup log reports inability to start `docker-agent.exe` because
  **"The paging file is too small for this operation to complete."** It also records a
  failed `wslinfo --networking-mode` command with an application exception.
* Docker Desktop/backend processes subsequently disappeared. Final CLI queries again
  fail because `dockerDesktopLinuxEngine` is absent. vmcompute and WSLService remain
  Running, but that does not establish a working Docker daemon.

Final free-space measurement: C: 115105792 bytes (about 110 MiB), D: 244867072
bytes (234 MiB), E: 86069248 bytes (82 MiB). Earlier read-only memory measurement:
about 1.8 GiB physical and 1.8 GiB virtual memory free. These are checkpoint values,
not permanent capacity measurements. Resource exhaustion is evidenced by the paging
error; it is not claimed as the sole proven cause of every WSL failure.

Further retries, image pulls or new database/app processes are inappropriate while
the engine is unstable and storage/commit capacity is exhausted. Changing paging,
WSL installation, system-wide configuration or deleting user data falls outside this safe
recovery. No such repair was attempted.

The launch-context diagnosis is consistent with a reported MSIX socket issue, but is
an inference, not a reproduced Codex packaging investigation:
[upstream report](https://github.com/openai/codex/issues/52698).
The Windows-service distinction is documented by
[Docker](https://docs.docker.com/desktop/setup/install/windows-permission-requirements/).

## Actual readiness and acceptance matrix

Configured Docs origin: `http://localhost:8082` (loopback `127.0.0.1:8082`).
**No usable live origin is currently verified.** Final HTTP checks for `/healthcheck`
and `/web-apps/apps/api/documents/api.js` fail with connection errors; no 8082 listener.
The daemon was unavailable for `docker ps`/volume enumeration, so existing container
configuration, image compatibility, volume state and runtime JWT equality remain unknown.
No Compose up, image pull, container create/reconfigure, manual container start or stop
was performed. Docker's transient startup rediscovered its existing container automatically.

| Required proof | Result |
| --- | --- |
| Stable Docker daemon / service inventory | Not achieved; final daemon unavailable |
| Docs healthy and API script available | Failed connection checks |
| Actual embedded LAC editor at 1366 x 768 | Not run; no editor screenshot produced |
| Letter editing | Not run |
| Real signed status-6/status-2 callbacks | Not run |
| Persisted DOCX bytes, hash, revision, reopen | Not run |
| Native Legal setup / alternating rulers / multipage flow | Not run |
| Browser -> Docs -> API and callback/save connectivity | Not run |
| JWT and view/edit negative acceptance | Not run |
| Office-to-canonical official note conversion | Not implemented or proven |
| Continuous official chain and clean physical printing | Not implemented or proven |

No new LAC host, account, sample data or disposable database was created. No simulated
callback test, HTML mockup, config response or static DOCX is substituted for real proof.
The old native test script is not a passing result: it has hardcoded 5088/5173 ports,
1440 x 900 screenshots, fixed waits and insufficient persisted-content assertions.
Do not run it against an existing database or interpret a loaded iframe as acceptance.

## Final physical policy for the next implementation

This new contract supersedes 45/20/25/20 for future Green Noting output. Preserve
`DelhiLacNotingV1`, existing DOCX bytes and historical submissions. Add an explicitly
versioned Green Legal v2 policy rather than retroactively modifying historical files.
No layout source or historical document was changed at this environment checkpoint.

| Property | Final requirement |
| --- | --- |
| Paper | Legal portrait, 215.9 x 355.6 mm; PDF MediaBox 612 x 1008 points |
| Odd page margins | Left 45 mm, right 15 mm |
| Even page margins | Left 15 mm, right 45 mm |
| Vertical margins | Top and bottom 25 mm |
| Usable rectangle | Width 155.9 mm, height 305.6 mm |
| Screen guide | Odd x=45 mm; even x=170.9 mm from left (45 mm from right) |
| DOCX dimensions | Width 12240, height 20160 twips; portrait |
| DOCX margins | Inside 2551, outside 850, top/bottom 1417 twips; mirrorMargins, gutter 0 |
| Physical print | 100%/actual Legal size; no red guide, green fill, binding decoration or clipping |

Use global page parity across the single composed chain, never note-local page numbers.
Officer B starts after A's unbroken handwritten-signature/identification/date/marking
reservation on the remaining space of that same page when it fits. Long text continues
onto subsequent pages. Neither note boundaries nor officer changes force a page break.
Keep the signature/marking block together; reserve its full measured height. Handwritten
signature space is not a digital signature. Existing official content is read-only.

Do not certify native ONLYOFFICE support for mirror margins, protected prior notes,
screen-only guides or exact printing without the real editor/print tests. Host CSS cannot
safely manufacture document-relative guides inside the cross-origin editor, and ordinary
document edit permission is not proof of immutable prefix protection.

## Exact next steps once a stable environment is available

1. Restore sufficient storage and Windows commit capacity through user/system administration;
   confirm WSL no longer faults. Do not prune/delete Docker volumes or reset distributions.
   Start installed Docker outside the packaged shell and require a stable responding daemon.
2. Inspect the existing 8082 container by ID/name, image, Compose labels, loopback binding,
   mounts and JWT enabled/secret equality. Compare secrets internally without printing them.
   If compatible, reuse it; otherwise start only the approved isolated service with preserved
   named volumes and existing `.env.onlyoffice`. No unrelated container actions.
3. Require healthcheck=true and API script HTTP 200. Only then create a disposable LAC
   regression database/storage and host from the verified isolated revision on unused ports.
   Disable operational bootstrap/background tasks and never inherit an operational connection.
   Use synthetic fixture identities/data in that disposable database; no existing-account changes.
4. Use the real `/matter-drafts/{draftId}` route and existing OnlyOfficeDraftEditor/service.
   Capture actual 1366 x 768 screenshots, type unique markers and several pages through the UI,
   trigger native save, and wait for observed persistence rather than fixed delays. Record real
   callback status/key, DOCX version/revision/hash and exact marker in stored bytes. Close all
   editor clients, observe final-save key rotation, reopen and verify markers visibly and in DOCX.
   Check missing/invalid JWT, view-only behavior and actual Docs/API network paths.
5. Test final v2 profile in newly created synthetic content only: odd/even native ruler positions,
   automatic overflow, same-page officer transition and keep-together signature block. Inspect
   actual print/export dimensions and appearance. Record unsupported capabilities explicitly.
6. Implement a correlated force-save barrier: session key/generation plus command userdata;
   durable callback document version/hash/revision; no "Server saved" before DB/storage commit.
   A command acceptance or editor state event alone is insufficient. Fail/timeout/conflict blocks
   submission; retry the same idempotent request after an uncertain outcome.
7. Add actor-bound server editor leases with live permissions, allocation, custody, open state
   and revision checks at config, download, callback, freeze and submit. Reject revoked/expired
   sessions and stale/late callbacks. Retain frozen content-addressed DOCX evidence independently
   of the mutable legacy document pointer; immutable snapshot metadata/history and audit.
8. Strictly validate/convert the frozen DOCX to the supported official JSON schema, with a
   versioned converter and explicit unsupported-structure errors. Preserve exact UTF-16 text,
   marks and paragraph delimiters; officer confirms canonical preview. No stale ContentJson,
   iframe scraping, InnerText flattening, guessed selection offsets or silent table/field loss.
   Preserve/revalidate citations by exact canonical range/hash; submitted remark anchors remain
   tied to immutable Note/version. Integrate through existing Dak -> Matter transaction,
   numbering, revision, idempotency and send rollback, never a separate official Office chain.
9. Use a server-owned continuous composer reading only the immutable official chain plus the
   authorized committed working note. Freeze renderer/font/layout versions and page breaks.
   Screen preview overlays guides/decorations outside authoritative PDF; PDF prints cleanly.
   If protected-prefix editing in ONE Office document cannot be guaranteed, author only the
   current note in genuine ONLYOFFICE beside the server's continuous physical preview. That
   preserves one authoritative history and same-page physical continuation without giving the
   Office editor authority to rewrite previous notes. Test this alternative end-to-end.

These are implementation gates, not newly available HTTP endpoints. Anti must continue to
use existing supported routes and must not invent save-status/import/viewer routes until the
backend publishes and implements their exact contract. Final UI remains Anti's responsibility.

## Preservation checks

Final listener PIDs equal the initial checks: API 5610 -> 11440, frontend 5611 -> 15020,
pilot PostgreSQL 55683 -> 10080. Ports 5190/5191 had no listeners and were not used.
Main, IIS, existing application databases, accounts, pilot source/config and protected ports
were not modified. No merge or production deployment. Production code remains at the verified
starting revision; this delivery adds only this checkpoint report, with its exact commit SHA
reported separately.

Local diagnostic sources, not uploaded externally: Docker host `com.docker.backend.exe.log`,
VM `init.log`, Windows Application events 1000/1001, process/service/listener inventories and
the existing Compose/environment metadata. No diagnostics archive or secrets were published.
