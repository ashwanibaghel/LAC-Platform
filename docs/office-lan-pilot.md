# LAC and ONLYOFFICE on the office PC

The development laptop has **no runtime role**. The office PC runs the existing
LAC IIS site, local PostgreSQL, document storage, and ONLYOFFICE Docs in Docker
Desktop. Browsers reach LAC through IIS and reach ONLYOFFICE on the same office
host at port 8082. ONLYOFFICE calls the IIS API for signed file downloads and
save callbacks.

```text
Office PC
├── IIS: LAC browser app and API, HTTP port 80
├── PostgreSQL: localhost only
├── document storage and backups
└── Docker Desktop: ONLYOFFICE Docs, office-host port 8082
```

The existing office IIS deployment is **Default Web Site**, using
**DefaultAppPool**, with physical path `C:\LAC-Publish` and an HTTP port 80
binding. This describes the current setup; verify these values on the office
PC before changing it. PostgreSQL remains localhost-only. Do not create a
firewall rule for TCP 5432. Restrict ports 80 and 8082 to approved office
clients according to the office network policy.

## Stage an IIS package

On the build machine, from the repository root:

```powershell
.\scripts\publish-office.ps1
```

The default output is `C:\LAC-Publish-New`, a **staged** self-contained win-x64
IIS package. The script runs `npx vite build`, copies the web bundle into API
`wwwroot`, then runs `dotnet publish -c Release`. The historical TypeScript
diagnostics do not prevent Vite from producing the production bundle. Use
`-FrameworkDependent` only when the office PC has the matching .NET runtime.
The package contains application files, not database passwords or JWT secrets.
It also contains `diagnose-office-auth.ps1` and the local `LAC.Api.exe`
maintenance commands. No maintenance command is exposed over HTTP.

Back up the office PostgreSQL database, the current `C:\LAC-Publish` deployment,
and document storage before an IIS swap. Startup may apply EF migrations. Verify
the staged package and IIS configuration before replacing the live files. The
publish script refuses to write directly to `C:\LAC-Publish`.

## Configure the office PC

Run ONLYOFFICE Docs with Docker Desktop **on the same office PC**, publishing
the container's HTTP port on office-host port 8082. Give the container
persistent storage and enable JWT. Use a production JWT secret shared with LAC;
store it in protected office-host configuration and **never commit it**. The
normal `docker-compose.onlyoffice.yml` is a loopback-only development example,
not the office-host deployment configuration.

Set these LAC environment variables through protected IIS/host configuration.
Replace `<OFFICE_HOST_LAN_IP>` with the office PC's current address or a stable
office DNS name in the actual deployment; it is not a fixed architecture value.

```text
OnlyOffice__Enabled=true
OnlyOffice__BrowserUrl=http://<OFFICE_HOST_LAN_IP>:8082
OnlyOffice__AppExternalUrl=http://<OFFICE_HOST_LAN_IP>
OnlyOffice__AppBrowserUrl=http://<OFFICE_HOST_LAN_IP>
OnlyOffice__DocumentServerUrl=http://<OFFICE_HOST_LAN_IP>:8082
OnlyOffice__JwtSecret=<protected secret>
```

The IIS origin serves both the browser app and `/api`. Configure the existing
`ConnectionStrings__DefaultConnection` to reach PostgreSQL on localhost and
keep the three `Storage__` paths on office-host persistent storage. Do not put
secrets in the staged package or in source-controlled files. The ONLYOFFICE
container must reach the IIS origin to fetch `office-file` and submit signed
callbacks; office browsers must reach port 8082.

## Diagnose office login before changing credentials

Run from an elevated or otherwise authorized PowerShell session **on the office
PC**, with the same effective environment as the IIS app pool. User-scoped
environment variables belonging to a different Windows account are not
automatically visible to IIS. First inspect the generated `web.config` and the
IIS app pool's environment settings; the doctor prints configuration source
and process/user/machine presence without printing secrets:

```powershell
.\diagnose-office-auth.ps1 -PackageDirectory C:\LAC-Publish -OfficePreflight -Username <admin-login> -HealthUrl http://localhost/api/health
```

Compare the `DB fingerprint` (host, port, database, database username),
`AppUsers count`, and target user state with the development laptop. A healthy
`/api/health` only proves that **some** configured database is reachable; it
does not prove it is the intended database or that its admin password matches.
For a password check, add `-VerifyPassword`. It prompts without echoing the
password. Alternatively set `LAC_AUTH_DOCTOR_PASSWORD` in a protected temporary
process environment; clear it in the calling shell immediately afterwards. Its output is only
`SUCCESS` or `FAILED`.

The `BootstrapAdmin__Username` and `BootstrapAdmin__Password` settings create
the first administrator **only when `AppUsers` is empty**. Changing the
bootstrap password in IIS after users exist does not reset any account.
Normal startup never updates an existing user's password. A 401 with an
existing, active user and a failed doctor password check therefore indicates
that the supplied password differs from the stored hash. An absent user or a
different DB fingerprint points to database/configuration instead.

For an authorized reset of an **existing** account, stop the IIS site or app
pool, open a local interactive console on the office PC, then run:

```powershell
Set-Location C:\LAC-Publish
.\LAC.Api.exe reset-admin-password --username <admin-login>
```

The command reads the new password without echo, hashes it using the same
ASP.NET PasswordHasher as login, updates `PasswordChangedAt`, and exits. It
will not create a user. Restart IIS, run the doctor with `-VerifyPassword`,
then test login in the office browser. Do not pass plaintext passwords as
command-line arguments or place them in `web.config`, the package, or source.

## Verify after the IIS swap

From the office PC and an approved office browser, check:

```text
http://<OFFICE_HOST_LAN_IP>/api/health
http://<OFFICE_HOST_LAN_IP>/
http://<OFFICE_HOST_LAN_IP>:8082/healthcheck
```

Log in through IIS, open a Letter and a Noting, type distinct markers, use the
native ONLYOFFICE Save control, close, and reopen each draft. Confirm the text
remains, the Noting Legal mirror-margin profile is intact, and Back to Matter
returns to the IIS-hosted Matter page. Verify direct refresh of a draft URL as
well as `/matter` and `/court` routes. Keep the feature branch until the office
deployment has passed these checks.
