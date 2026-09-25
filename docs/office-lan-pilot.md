# Office LAN pilot

This urgent pilot runs the published LAC application on the office host and temporarily uses ONLYOFFICE Docs running on the development laptop. Clients use the office host at one origin; ONLYOFFICE receives server-to-server file and callback traffic using the two LAN addresses below.

```text
Office client -> http://<OFFICE_SERVER_LAN_IP>:5088
Office LAC host -> http://<LAPTOP_LAN_IP>:8082  (ONLYOFFICE)
Laptop ONLYOFFICE -> http://<OFFICE_SERVER_LAN_IP>:5088 (callback and office-file)
```

PostgreSQL remains localhost-only on the office host. Do not expose TCP 5432.

## Build the source-free package

On the development build machine, from the repository root:

```powershell
.\scripts\publish-office.ps1 -PublishDirectory C:\LAC-Office-Pilot-Package
```

The script uses `npx vite build`, copies `dist` into API `wwwroot`, then produces a self-contained Windows x64 application. It does not require Node, npm, source code, or the .NET runtime on the office target. The existing `npm run build` path is intentionally not used because the repository has historical TypeScript diagnostics even though Vite production bundling succeeds.

Copy the resulting folder to `C:\LAC-Publish` on the office host. Never copy `.env.onlyoffice`, user secrets, credentials, database passwords, or JWT secrets.

The publish directory is application code only. Use these data locations:

```text
D:\Software Data\Documents
D:\Software Data\Extraction
D:\Software Data\DatabaseBackups
D:\Software Data\Logs
```

Before pointing an existing database at the pilot, create and verify a PostgreSQL backup. Startup applies EF migrations. A new empty pilot database has no data to back up.

## Office-host configuration and start

Copy `office-pilot-settings.template.ps1` to `office-pilot-settings.ps1` beside `LAC.Api.exe`, set its values securely, then run:

```powershell
Set-Location C:\LAC-Publish
.\start-office-pilot.ps1
```

Required settings are `ConnectionStrings__DefaultConnection`, the three `Storage__` paths, `OnlyOffice__Enabled=true`, `OnlyOffice__BrowserUrl`, `OnlyOffice__AppExternalUrl`, `OnlyOffice__DocumentServerUrl`, and `OnlyOffice__JwtSecret`. The start script never prints secrets. The ONLYOFFICE URLs must use the exact LAN IPv4 addresses shown above, and the API and Document Server must share one fresh pilot JWT secret.

For a permanent IIS deployment, install the ASP.NET Core Hosting Bundle and configure IIS separately. IIS is not required for this Kestrel pilot.

## Temporary laptop ONLYOFFICE LAN mode

Do not change `docker-compose.onlyoffice.yml`; it remains localhost-only development configuration. On the development laptop, create an untracked pilot environment file containing `ONLYOFFICE_LAN_IP=<laptop LAN IPv4>` and a fresh `ONLYOFFICE_JWT_SECRET`, then run:

```powershell
docker compose --env-file .env.onlyoffice.lan-pilot -f docker-compose.onlyoffice.lan-pilot.yml up -d
```

The separate compose file binds only the selected laptop LAN IPv4 on TCP 8082. It is a temporary pilot, never public-internet exposure. For an office Windows Server 2016+ permanent installation, use ONLYOFFICE Docs’ native Windows Server deployment route; Docker Desktop is not the production plan. For Windows 10/11 office hosts, keep this laptop topology until permanent hosting is decided.

## Firewall

Run as Administrator with the approved private subnet, for example `192.168.10.0/24`:

```powershell
# Office LAC host
.\office-pilot-firewall.ps1 -Role LacHost -ApprovedSubnet 192.168.10.0/24

# Development laptop running ONLYOFFICE
.\office-pilot-firewall.ps1 -Role OnlyOfficeLaptop -ApprovedSubnet 192.168.10.0/24
```

This creates Private-profile inbound rules only: TCP 5088 to the LAC host and TCP 8082 to the laptop. Do not add a PostgreSQL 5432 rule.

## Acceptance checks

On the office host, verify `http://localhost:5088/api/health`. From an approved LAN client, verify:

```text
http://<OFFICE_SERVER_LAN_IP>:5088/api/health
http://<OFFICE_SERVER_LAN_IP>:5088/
http://<LAPTOP_LAN_IP>:8082/healthcheck
```

Then log in, refresh `/matter`, `/court`, and a `/matter-drafts/{id}` route directly, create a Letter and a Noting, open ONLYOFFICE, edit, save, close, and reopen. The React fallback serves browser routes while unmatched `/api` routes remain 404s.