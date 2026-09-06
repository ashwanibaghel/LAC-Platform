# Local development (Windows)

LAC Platform is local-first. It needs no Supabase account or internet connection for normal development. The API talks only to a local PostgreSQL instance and document binaries stay on local or approved network storage.

## 1. PostgreSQL prerequisite

Install PostgreSQL 17 for Windows (the current Npgsql/EF Core provider is compatible with it). Keep the default port **5432**, locale, and service installation. Choose a strong password for the PostgreSQL administrator account and keep it outside the repository.

Open **SQL Shell (psql)** and run, replacing the placeholder password locally:

```sql
CREATE ROLE lac_app LOGIN PASSWORD 'choose-a-local-secret';
CREATE DATABASE lac_platform OWNER lac_app;
```

Do not use the `postgres` administrator account for the application. Keep PostgreSQL listening on `127.0.0.1`; office browsers use IIS/the API, never PostgreSQL directly.

## 2. Configure local secrets

Set user-level environment variables in PowerShell. These values are examples; do not commit them.

```powershell
[Environment]::SetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Host=127.0.0.1;Port=5432;Database=lac_platform;Username=lac_app;Password=YOUR_LOCAL_SECRET;Pooling=true', 'User')
[Environment]::SetEnvironmentVariable('Storage__DocumentRoot', 'C:\LAC-Data\Documents', 'User')
[Environment]::SetEnvironmentVariable('Storage__ExtractionRoot', 'C:\LAC-Data\Extraction', 'User')
[Environment]::SetEnvironmentVariable('Storage__BackupRoot', 'C:\LAC-Data\Backups', 'User')
[Environment]::SetEnvironmentVariable('PdfImport__MaxFileSizeMb', '250', 'User')
[Environment]::SetEnvironmentVariable('PdfImport__MaxConcurrentJobs', '1', 'User')
[Environment]::SetEnvironmentVariable('Ocr__Enabled', 'true', 'User')
[Environment]::SetEnvironmentVariable('Ocr__TesseractExecutable', 'C:\Program Files\Tesseract-OCR\tesseract.exe', 'User')
[Environment]::SetEnvironmentVariable('Ocr__PdfToImageExecutable', 'PATH_TO_LOCAL_PDFTOPPM_EXE', 'User')
[Environment]::SetEnvironmentVariable('Ocr__Language', 'eng', 'User')
```

Restart terminals/IIS after changing environment variables. The document root can instead be `D:\LAC-Documents` or an approved UNC path such as `\\LAC-STORAGE\Documents`; no domain code changes are needed.

## 3. Run locally

```powershell
cd C:\LAC-Platform
dotnet restore
dotnet ef database update --project src\LAC.Infrastructure --startup-project src\LAC.Api --context LacDbContext
dotnet run --project src\LAC.Api --urls http://127.0.0.1:5088
```

In another terminal:

```powershell
cd C:\LAC-Platform\src\LAC.Web
npm install
npm run dev
```

Open `http://127.0.0.1:5173` and check `http://127.0.0.1:5088/api/health`. A healthy response reports API/database/document-storage status without revealing connection strings or storage paths.

## PDF/OCR and size limits

PDF upload is streamed directly to document storage in 128 KiB chunks and SHA-256 is calculated while streaming; the application does not buffer a 100–250 MB upload in memory. The default 250 MB limit is enforced by Kestrel and the API. For IIS, set the site `maxAllowedContentLength` to at least `262144000` bytes (or the approved higher configured limit) as well.

Extraction uses one background job at a time by design for an 8 GB pilot PC. OCR is optional and local-only: install Tesseract with the English `eng` language file and a local Poppler `pdftoppm.exe` renderer, then set the four `Ocr__...` variables above. The application creates temporary local render files only within `Storage__ExtractionRoot` and deletes them when the job ends. Do not configure cloud OCR.

## Backup

Database and documents are separate. A database dump alone does **not** contain PDFs. Example database backup:

```powershell
pg_dump -h 127.0.0.1 -U lac_app -Fc -f C:\LAC-Data\Backups\lac_platform.backup lac_platform
```

Also copy the complete configured DocumentRoot to the backup destination, retaining directory structure and access controls.
