# Office LAN pilot deployment

The IIS host serves both React and `/api` from one site. Office clients need only a browser; PostgreSQL remains on the host at `127.0.0.1` and must never be opened to the LAN.

## Prerequisites and folders

Install Git, .NET 10 SDK, ASP.NET Core Hosting Bundle 10, IIS, Node/npm, and PostgreSQL 17. Install Python 3.11 only when the local OCR worker is enabled.

```text
C:\LAC-Platform                 source
C:\LAC-Publish                  IIS application
D:\Software Data\Documents
D:\Software Data\Extraction
D:\Software Data\DatabaseBackups
D:\Software Data\Logs
```

Use a localhost-only PostgreSQL configuration (`listen_addresses = '127.0.0.1'`). Do not create a LAN firewall rule for port 5432.

## Configuration

Set production values as machine/IIS environment variables, never in `appsettings*.json` or Git:

```text
ConnectionStrings__DefaultConnection
Storage__DocumentRoot=D:\Software Data\Documents
Storage__ExtractionRoot=D:\Software Data\Extraction
Storage__BackupRoot=D:\Software Data\DatabaseBackups
DocumentIntelligence__Enabled=true
DocumentIntelligence__PythonExecutable=C:\LAC-Platform\tools\document-intelligence-worker\.venv\Scripts\python.exe
DocumentIntelligence__WorkerScript=C:\LAC-Platform\tools\document-intelligence-worker\worker.py
DocumentIntelligence__WorkingDirectory=C:\LAC-Platform\tools\document-intelligence-worker
```

Keep database passwords only in protected machine/IIS configuration. The existing RapidOCR/Torch local runtime is used as-is; do not substitute cloud or ONNX services.

## Publish and IIS

From the source checkout run:

```powershell
.\scripts\publish-office.ps1
# or .\scripts\publish-office.ps1 -PublishDirectory C:\SomeOtherPublishFolder
```

The script builds React, stages its generated files in the API `wwwroot`, and publishes the same-site application. `wwwroot` is generated and ignored by Git. Configure IIS with physical path `C:\LAC-Publish`, an app pool using **No Managed Code**, and the Hosting Bundle installed. Grant `IIS AppPool\<AppPoolName>` Read/Execute on the publish directory and worker runtime; grant Modify on `D:\Software Data`.

Restrict IIS firewall access to approved LAC client PCs/subnets; do not blindly expose the application to the entire office network.

## Verify and operate

1. Open `/api/health`, then `/api/health/document-intelligence` (the latter reports non-secret worker readiness without OCR or data writes).
2. Open the homepage locally and from an approved client PC.
3. Upload a dummy PDF and confirm it lands under `D:\Software Data\Documents`.
4. Restart/recycle the IIS app pool and repeat the health check.

Analyze remains background work: its status is Queued, Processing, Completed/Review ready, or Failed. Failed jobs show a short actionable reason; use the worker health endpoint before retrying. Never send real government PDFs to cloud services.
