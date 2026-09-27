# Matter Drafts with ONLYOFFICE (development POC)

Branch: `codex/onlyoffice-matter-drafts`, created from exactly
`30b1c943f6cd4eb1b3e8973f8e90bcf8caa253c7`. No merge or main changes.

## Start locally (PowerShell 7)

Prerequisites: Docker Desktop with Linux containers, .NET 10, Node.js/npm,
and the existing LAC local PostgreSQL database/configuration. Stop any old LAC
API on port 5088 before starting this branch. API startup applies EF migrations.
Back up the database and existing document volume before applying migrations to
valuable data. Use your normal LAC account; this integration adds no login credentials.

From the repository root, generate a local secret **once** (keep it for restarts):

```powershell
Set-Location C:\LAC-Platform-onlyoffice
if (-not (Test-Path .env.onlyoffice)) {
    $officeSecret = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
    Set-Content -LiteralPath .env.onlyoffice -Value "ONLYOFFICE_JWT_SECRET=$officeSecret"
    Remove-Variable officeSecret
}
$env:ONLYOFFICE_JWT_SECRET = (Get-Content -LiteralPath .env.onlyoffice | Where-Object { $_ -like 'ONLYOFFICE_JWT_SECRET=*' }) -replace '^ONLYOFFICE_JWT_SECRET=', ''
docker compose --env-file .env.onlyoffice -f docker-compose.onlyoffice.yml up -d
docker compose --env-file .env.onlyoffice -f docker-compose.onlyoffice.yml ps
Invoke-RestMethod http://localhost:8082/healthcheck
```

The first startup/image download can take several minutes. The health check must
return `true`. If it fails, inspect the container logs; do not disable JWT.
The compose file binds Docs to loopback port 8082 and enables
`ALLOW_PRIVATE_IP_ADDRESS` **only for development**, so Docs can fetch files and
send callbacks to `host.docker.internal`. Production needs explicit network
policy, HTTPS, a deployment-managed secret, and a reachable approved Docs origin.

In that same terminal, configure LAC and run it:

```powershell
$env:OnlyOffice__Enabled = 'true'
$env:OnlyOffice__BrowserUrl = 'http://localhost:8082'
$env:OnlyOffice__AppExternalUrl = 'http://host.docker.internal:5088'
$env:OnlyOffice__AppBrowserUrl = 'http://localhost:5173'
$env:OnlyOffice__JwtSecret = $env:ONLYOFFICE_JWT_SECRET
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/LAC.Api -c Release --no-launch-profile --urls http://0.0.0.0:5088
```

Use the existing `Storage:DocumentRoot` and `ConnectionStrings:DefaultConnection`
configuration. If a database connection has not been configured, set it before
running the API (enter your own local PostgreSQL connection string):

```powershell
dotnet user-secrets set 'ConnectionStrings:DefaultConnection' (Read-Host 'Local PostgreSQL connection string' -MaskInput) --project src/LAC.Api
```

In another terminal:

```powershell
Set-Location C:\LAC-Platform-onlyoffice\src\LAC.Web
npm ci
npm run dev -- --host 127.0.0.1 --port 5173
```

Open [LAC](http://localhost:5173), log in normally, open an existing Matter,
create a Letter or Noting draft, and open it. Allow local firewall access from
Docker Desktop to the API if your machine blocks that traffic.

To stop Docs without deleting its persistent volumes:

```powershell
docker compose --env-file .env.onlyoffice -f docker-compose.onlyoffice.yml down
```

## Configuration and routing

`OnlyOffice` is strongly typed, disabled by default and validated on startup.
When enabled, URLs must be HTTP(S) origins without credentials, query strings or
paths, and the secret must contain at least 32 UTF-8 bytes. Environment variables
use the standard double-underscore convention. `.env.onlyoffice.example` has
placeholders only; `.env.onlyoffice` is ignored. ASP.NET does not automatically
load dotenv files: the commands above explicitly set its environment.

- `BrowserUrl`: browser-to-Docs script/editor URL.
- `AppExternalUrl`: Docs-to-LAC API origin. It differs from BrowserUrl.
- Optional `AppBrowserUrl`: browser-facing LAC origin for ONLYOFFICE's Back to Matter button;
  defaults to AppExternalUrl when the UI and API share an origin. Set it to the
  actual web app origin when they differ, as in local Vite development.
- The signed editor config selects display zoom from the browser viewport height:
  85% at 800px or below, 90% at 801–950px, and 100% above 950px. Users can
  change zoom in ONLYOFFICE; DOCX page size and margins are unaffected.
- `JwtSecret`: same secret as container `JWT_SECRET`; never sent to the browser.
- Optional `DocumentServerUrl`: exact origin LAC permits for callback downloads;
  defaults to BrowserUrl. When using this override, configure Docs/proxy so the
  **signed callback URL it emits** uses this origin. LAC never rewrites signed URLs.

Routes remain under `/api/matter-drafts/{id}`. `office-config` requires a current,
active LAC user with scoped Draft.View on the parent Matter. Scoped Draft.Edit
sets edit/review/comment permissions and edit mode; otherwise mode is view.
Creation keeps Draft.Create authorization. The React route dynamically loads
Docs' API script, destroys its editor on unmount and uses the full viewport below
a small LAC header. All formatting, pagination, rulers, zoom and printing belong
to ONLYOFFICE. The host config type also supports `cell`/`xlsx` for future work.

## Storage and migration

Migration `20260924214843_AddMatterDraftOfficeDocument` adds nullable
`OfficeDocumentId` (restricted-delete FK to Documents), `OfficeKeyGeneration`,
and the last save fingerprint. `ContentJson` remains unchanged.

With integration enabled, newly created Letter/Noting drafts immediately receive
a real Open XML DOCX through the existing `IDocumentStorage.SaveAndHashAsync`.
Existing drafts materialize once on first opening. Text paragraphs, headings,
hard breaks and paragraphs nested in lists/tables are preserved. Legacy table
geometry, marks, page wrappers and advanced formatting are not migrated by this
POC; original JSON is retained. No text is invented. Letter uses its stored page
profile (default A4 portrait, 20 mm); Noting uses A4 portrait, 25/20/20/25 mm.

The existing Document is the stable file identity. It owns filename, storage
path, size, SHA-256 and version. Draft ownership remains the MatterDraft FK;
there is no automatic MatterDocument link, avoiding an alternate authorization
path through the Matter document list. A draft's legacy PUT is rejected once
OfficeDocumentId exists. When disabled, unconverted drafts retain the legacy
editor; converted drafts show that ONLYOFFICE must be enabled, never stale JSON.

Saves use a temporary file, a 50 MiB stream limit, a 200 MiB expanded ZIP limit,
Open XML package inspection and rejection of macro-enabled documents. A complete
replacement file is written with the **existing storage service**. One EF
SaveChanges transaction atomically replaces Document.StoragePath, hash, size,
version, timestamps and draft revision/key metadata. The Document ID stays the
same. Readers see the old complete file or the new complete file; no in-place
overwrite can leave old metadata pointing at partially written bytes. The prior
file is deleted only after commit. This is an atomic database pointer replacement,
not an in-place filesystem overwrite. Successful saves retain one physical copy.

On uncertain database failures or a process crash, old/staged files can remain
for recovery. Do not delete files referenced by Documents. A reconciliation job
is outside this POC. The existing EF audit mechanism records metadata changes;
Document.Version counts saves, but this spike does not add historical byte copies.
Revision concurrency checks also protect competing server processes; per-process
bounded locks serialize opens and callbacks. Concurrent commit conflicts return
an error/retry rather than overwriting a committed document silently.

## Signed requests and save semantics

Editor JWTs use HS256 with the complete config as payload, as specified by
[ONLYOFFICE's browser signature API](https://api.onlyoffice.com/docs/docs-api/additional-api/signature/browser/).
`office-file?token=...` uses a separate token shape with draft ID, document ID,
purpose `onlyoffice-download`, and a ten-minute expiration. It requires no browser
cookie, checks active records and serves only that DOCX. Responses are no-store.
Do not log query tokens at a reverse proxy. A config left unopened longer than
ten minutes needs reloading to obtain a fresh file URL.

Callbacks accept an Authorization Bearer JWT or a body token. HS256 and signature
are required; supplied expiration/not-before claims are checked. ONLYOFFICE does
not require those temporal claims on every callback, so session key binding is
also enforced. Only authenticated payload fields are used, including the
[`payload` wrapper used by header tokens](https://api.onlyoffice.com/docs/docs-api/additional-api/signature/request/token-in-header/).
Browser-config/download tokens cannot act as save callbacks. Download URLs must
match the exact configured Docs scheme, host and port; credentials/fragments and
redirects are rejected. DI HttpClient has timeouts, no cookies and no URL logging.

| Status | Behavior |
| --- | --- |
| 1 | Editing; acknowledge, no replacement |
| 2 | Validate/download/persist final file; increment key generation |
| 3, 7 | Log draft/status diagnostics; retain last good file; acknowledge |
| 4 | Closed unchanged; acknowledge, no replacement |
| 6 | Persist force save and revision/version; **keep the same editing key** |

Successful callbacks return `{"error":0}`; failures return a nonzero error
(invalid/missing signatures also use HTTP 401). The last successful callback
fingerprint handles final-save retries after key rotation. Force saves always
download and validate again, and skip replacement only when the bytes' SHA-256
matches the stored file, so a reused URL cannot lose new changes. Stale session
keys are rejected. Keys are `lac-draft-{id}-g{generation}`.
This follows the [callback contract](https://api.onlyoffice.com/docs/docs-api/usage-api/callback-handler/).
Autosave and forcesave are enabled; there is no separate LAC save button or claim
that in-editor changes are already persisted.

## Verification and live acceptance

```powershell
dotnet build -c Release
dotnet test -c Release
dotnet ef migrations has-pending-model-changes --project src/LAC.Infrastructure --startup-project src/LAC.Api --configuration Release
Set-Location src/LAC.Web
npx tsc -b
npx oxlint
npm run build
```

The API tests use generated test-only credentials/secrets, isolated in-memory
databases, real local DOCX storage and a simulated Docs download handler. They
exercise creation, lazy migration, package validity/profiles, RBAC, signed URLs,
callback statuses, force/final key behavior, physical file/hash updates, retries,
invalid URLs/downloads and protection against legacy JSON overwrites. They are
**not** an actual ONLYOFFICE/browser acceptance test or a PostgreSQL durability test.

Live acceptance still requires all of the following on a Docker-equipped machine:

1. Create Letter and Noting drafts and confirm embedded Docs opens.
2. Type at least five pages; use native rulers on each page, change all four
   margins, insert a table and page break, change zoom, and print.
3. Press Save; verify status 6 changes stored bytes/hash/version/revision while
   the key remains unchanged. A second opening joins the same session.
4. Close every editor tab; allow Docs' final-save delay; verify status 2 persists
   bytes and advances generation. Reopen and confirm all content/layout persists.
5. Restart LAC and reopen; inspect the DOCX with Word/LibreOffice if available.
6. Repeat with separate view-only/edit users; view-only must not edit.

Record browser screenshots, callbacks, hash/version evidence and the resulting
five-page DOCX. Do not label the integration verified until this is done.

## Recorded results — 25 September 2026

**CODE COMPLETE for the POC; LIVE INTEGRATION NOT VERIFIED.** Frontend release
quality remains blocked by existing TypeScript errors in the requested base.

- Final isolated checkout: `C:\LAC-Platform-onlyoffice` on the requested branch,
  directly based on `30b1c943f6cd4eb1b3e8973f8e90bcf8caa253c7`.
- `dotnet build`: passed, zero errors; three existing xUnit analyzer warnings.
- `dotnet test --no-build`: **532 passed, zero failed, zero skipped**, including
  20 ONLYOFFICE cases and the repeated-force-save-URL regression assertion.
- EF `has-pending-model-changes`: no pending changes. Migration is generated;
  production/local PostgreSQL migration was not applied during this task.
- `npx tsc -b --force`: fails with **38 errors**. A clean archive of the exact
  starting commit produces byte-for-byte identical diagnostics: zero new errors.
- `npx oxlint`: exit 0, 88 existing warnings. The new editor file has no warnings.
- `npm run build`: fails at the same TypeScript gate. `npx vite build` separately
  succeeds, with the existing large-bundle warning. This is not a passing npm build.
- New/lazy DOCX packages validate with the Open XML validator. Simulated signed
  callbacks physically update files, hashes, revisions and versions. Reopening
  through config returns the preserved/rotated key as appropriate. API tests
  exercise live database view/edit permissions and deny unauthorized opens.
- Docker Desktop, Docker CLI and Podman are unavailable, including in WSL.
  ONLYOFFICE was **not opened**. Real browser editing, five-page pagination,
  rulers, margins, tables, page breaks, zoom, printing, native read-only behavior,
  final-close callbacks and persistence across an application restart remain
  **unverified**. No five-page acceptance DOCX or browser screenshots are claimed.
- The shared checkout was externally switched during work. Implementation was
  transferred into the isolated worktree, and the shared source changes were
  removed. No abandoned editor files or later-commit credentials were copied.
- Corrupted local npm dependencies were reinstalled. DLL locks/disk-space issues
  were resolved using isolated builds and `dotnet clean`; results above are from
  the isolated checkout after those recoveries.

Changed files (20; the generated migration designer accounts for most lines):

| Area | Files |
| --- | --- |
| Configuration/docs | `.gitignore`, `.env.onlyoffice.example`, `docker-compose.onlyoffice.yml`, `docs/onlyoffice-development.md` |
| API | `src/LAC.Api/Program.cs`, `MatterEndpoints.cs`, `OnlyOfficeEndpoints.cs` |
| Domain | `src/LAC.Domain/Entities.cs` |
| Infrastructure | `src/LAC.Infrastructure/Configurations/MatterConfiguration.cs`, `MatterDraftDocx.cs`, `OnlyOfficeOptions.cs`, `OnlyOfficeTokens.cs`, `OnlyOfficeDraftService.cs` |
| EF | `20260924214843_AddMatterDraftOfficeDocument.cs`, its `.Designer.cs`, `LacDbContextModelSnapshot.cs` under `src/LAC.Infrastructure/Migrations` |
| Frontend | `src/LAC.Web/src/App.tsx`, `editor/OnlyOfficeDraftEditor.tsx`, `editor/onlyoffice-editor.css` |
| Tests | `tests/LAC.Tests/OnlyOfficeTests.cs` |

The final commit SHA is reported in the task response. No merge is performed.
