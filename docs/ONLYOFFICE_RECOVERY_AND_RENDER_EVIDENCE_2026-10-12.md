# ONLYOFFICE recovery and real renderer evidence

12 October 2026, Asia/Calcutta. Tested application source SHA:
`9b04590039fe4069680c95f7d6f69be273ad21ea`.
Branch: `codex/dak-matter-foundation-20261010`.
This historical checkpoint was followed by `ONLYOFFICE_REAL_ACCEPTANCE_RESULTS_2026-10-12.md`.
The later checkpoint resolves frontend access, proves native save/reopen/printing and
corrects the font-box versus printed-ink measurement below. Official Green Noting
integration remains incomplete. Status observations below describe this earlier run.
This report records partial feasibility evidence. Official Green Noting integration is
not implemented or certified. No backend source fix, merge or deployment was made.

## Recovery and resource limits

Safe cleanup receipts and the complete classification inventory are at
`E:/LAC-Space-Recovery-20261012/DISK_RECOVERY_REPORT_2026-10-12.md`.
Only verified inactive .NET outputs, generated Gradle transforms/API JARs, Conda's
archive/index cache, the separately approved 79 VS Code installer ZIPs and ordinary
lock-protected uv prune were removed. Sources, acceptance evidence, installed
environments, documents, databases, Docker/WSL disks and volumes were preserved.
No forced uv pruning, Docker pruning, reset, reinstall, pagefile configuration change
or reboot occurred. uv reported 29.2 GiB of logical files removed; hardlinks mean this
is not a physical reclaimed-space claim.

After Docker, automatic pagefile growth and test preparation, the latest C: free space
was 5,427,920,896 bytes (5.06 GiB), a net gain of 5,309,345,792 bytes (4.94 GiB) against
the initial 118,575,104 bytes. D: free 244,867,072; E: free 83,116,032. The requested
15–20 GB headroom was not achieved. No protected data was deleted to meet a target.
Latest available physical/virtual memory: approximately 783/2798 MiB. The automatically
managed pagefile grew from 9932 to 13040 MiB without a configuration change.

Three approved Colab MCP process groups were paused for uv pruning. Their host-owned
transport has not successfully reconnected: the supported connector call returned
`Transport closed`. Other MCP servers and the Codex app server were preserved. An
unconnected background uvx process was not substituted for the real transport.

Docker Desktop reached daemon 29.8.0 using the installed per-user WSL engine. The
existing container `lac-platform-onlyoffice-onlyoffice-1` was reused, not replaced.
Image `onlyoffice/documentserver:9.4`, installed package 9.4.0-129; existing volumes
and JWT configuration preserved. Loopback binding is `127.0.0.1:8082:80`.
Both docservice and converter initially failed because
`/etc/onlyoffice/documentserver/log4js/production.json` was empty. Its preserved empty
original was replaced with the exact official source matching the installed package's
conffile MD5 `9f47e0699ef29e4f105b3e2cbd8aabfb`; original owner/mode restored. Only
those two failed services were started. `local.json` hash was unchanged. Disk exhaustion
may explain the truncation but the writing event was not witnessed.

Docs health: `http://localhost:8082/healthcheck` HTTP 200, `true`; Docs API script
HTTP 200. Final container uptime 32 minutes, both services running for 27 minutes,
restart count unchanged. JWT enabled and equality with
`C:/LAC-Platform-onlyoffice/.env.onlyoffice` were checked privately; no secret is in
this report or repository.

## Isolated application status

The user explicitly approved the exact test launch after the first automatic review
rejection. `launch-api.ps1` subsequently succeeded:

* API `http://127.0.0.1:5920`, PID 26664.
* Disposable PostgreSQL 17 on `127.0.0.1:56683`, PID 21596, database
  `lac_onlyoffice_real_9b0459_20261012`; no operational connection used for test writes.
* Build, database, document/extraction/backup storage and fixtures are under
  `C:/Users/ashwa/.codex/artifacts/onlyoffice-real-feasibility-9b0459-20261012`.
* Bootstrap and configured background workers disabled. Source baseline build passed
  with zero warnings/errors. Runtime queries are confined to this disposable database.
* Actual frontend build was verified against all 142 tracked source/package files at
  the baseline; all hashes matched. It has not been modified or replaced with a mock.

However, automatic approval review again rejected starting that actual frontend on
`127.0.0.1:5921`, reporting only `blocked by policy`. No equivalent retry or alternate
host was used to bypass that rejection. Port 5921 has no listener. The planned origin
`http://onlyoffice-feasibility.localhost:5921` is therefore NOT ready or reachable.
The embedded editor, user typing, genuine save callbacks, reopen and 1366×768 editor
screenshots remain blocked. Existing API and Docs processes remain running.

Synthetic identities exist only in the disposable database. Credentials are available
privately in the local fixture manifest, not repeated in this current report:

| Account | Capability |
| --- | --- |
| office.feasibility.editor | Authorized draft editing |
| office.feasibility.viewer | Draft view only |

The supported editor route, when the real frontend launch is resolved, is
`/matter-drafts/{draftId}`. A4 letter ID `d6c28346-d52b-40ae-a536-cf1b1ce53cf8`;
Legal fixture ID `6e926c6e-ed42-4eee-83b9-b893c4ce7c4d`.
Never substitute `/editor?documentId=...`.

## Real API/security observations

The actual cookie login endpoint returned HTTP 200 for both synthetic accounts.
Actual office-config responses returned mode/edit permission `edit/true` for the
editor and `view/false` for the viewer; configuration included signed JWTs and the
existing Docs API script origin. Signed document download returned HTTP 200 for both
authorized accounts. Unauthenticated office-config returned 401. Missing and invalid
callback JWTs returned HTTP 401 with `{"error":1}`. No fabricated signed save callback
was sent. These observations do not prove read-only behavior in the editor canvas or
session revocation safety.

Document Server's own HTTP client reached `host.docker.internal:5920/api/auth/me`
and received 401, proving the loopback API is reachable from the existing container.
The subsequent real conversion fetched the authorized DOCX through that API.
Evidence: `api-security-evidence.json` in the test artifact root. JWTs, cookies and
signed download URLs are excluded from saved summaries.

## Real Legal rendering observations

The actual ONLYOFFICE `/converter` service accepted an HS256-authenticated request,
downloaded the Legal fixture from LAC's signed office-file endpoint and returned a
finished PDF. This uses the documented [conversion API](https://api.onlyoffice.com/docs/docs-api/additional-api/conversion-api/request).
It is a renderer/network feasibility test, not editor save/reopen acceptance.

PDF: `legal-v2-real-onlyoffice.pdf`, 38,864 bytes, SHA256
`c3115e11852600e02d66f6fe50a359855789507dc4e602def9fd324f6a6ff53d`.
Input DOCX SHA256:
`7256e1dedf5fcabc6fe66543af436a93eeaef6ec1daf8982dffd76d646bcee30`.
Input XML has portrait 12240×20160 twips; left/right 2551/850 twips;
top/bottom 1417 twips; gutter 0; mirrorMargins; zero forced page breaks.
This final-policy synthetic document is intentionally a Letter draft so that the
legacy Noting v1 callback cannot silently normalize it to the historical profile.
It is not an official submitted Green Note Sheet.

All five rendered pages have MediaBox 612×1008 pt. Measured left text position:
odd 44.997 mm; even 14.993 mm. Officer A, reserved signature/identification/date/
forwarding space and Officer B appear on page one in that order. Long paragraphs
continue automatically across pages. Every rendered page was visually inspected;
no clipping/overlap, red guides or green background appeared. PDF inspection found
black text only, no images, rectangles or lines.

**Finding:** page four's lowest glyph bounds reach 24.241 mm from the bottom despite
the DOCX 25 mm bottom-margin setting. Strict ink clearance is therefore not certified:
investigate native line metrics and pinned renderer/font behavior before acceptance.
This is approximately 0.76 mm intrusion, not evidence of a wrong paper size. Right
ink bounds need not equal the right margin because ragged lines end before it.

Evidence: `legal-conversion-evidence.json`, `legal-layout-evidence.json`, five
`legal-page-N.png` renders and `inspect-legal-pdf.py` in the test artifact root.
PDF extraction removes some whitespace; fixture label checks normalize whitespace
only for this test. PDF extraction must never supply canonical note anchors.

Both drafts still have revision 1, document version 1 and key generation 0, with
unchanged baseline hashes (`database-baseline.csv`). This explicitly confirms that
there is no save callback persistence or reopen evidence yet.

## Remaining integration gates and next steps

1. Resolve the frontend launch review block. Then use the real supported routes at
   1366×768, type a unique marker, observe genuine signed status-6 and final status-2
   callbacks, and retain original/result bytes, SHA, revision, version and key rotation.
   Close all editor clients, reopen and verify the marker both visibly and in stored
   DOCX. Test actual read-only canvas behavior. No fixed delay substitutes for DB proof.
2. In the real editor test native Legal settings, odd/even ruler margins and multipage
   editing. Re-export after editing and investigate the bottom ink-margin finding.
   Red alternating guides and green screen decoration require a separate screen-only
   layer; native support has not been demonstrated. Never print those decorations.
3. Implement the previously published correlated save barrier and durable save receipt;
   actor-bound editor leases with current permissions/custody/revision checks; frozen
   content-addressed immutable DOCX snapshots; strict versioned DOCX-to-canonical-note
   conversion with unsupported-structure errors and officer confirmation; and integration
   into the existing transactional official note/send workflow. Current callbacks update
   mutable DOCX pointers and counters, not the authoritative canonical submitted chain.
4. Compose one server-owned continuous official sheet with global page parity and
   measured signature reservation. If reliable immutable-prefix protection cannot be
   demonstrated inside ONLYOFFICE, edit only the current note there beside the continuous
   server preview. Never create two independent official noting systems.
5. Require a storage-backed save receipt before showing "Server saved". Offline unsaved
   typing must not be advertised as restart-safe; persistent browser draft caching stays
   disabled. These are implementation gates, not already implemented endpoints/features.

Protected API 5610 PID 11440, frontend 5611 PID 15020 and old pilot PostgreSQL 55683
PID 10080 stayed unchanged. Ports 5190/5191 remained unused. Main, IIS, original
accounts, protected databases and existing acceptance services were not altered.
