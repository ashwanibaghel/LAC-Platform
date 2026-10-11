#requires -Version 5.1
[CmdletBinding()]
param(
    [string]$DataDirectory,
    [int]$PgPort = 55440,
    [int]$ApiPort = 5399
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $DataDirectory) {
    $DataDirectory = Join-Path $env:TEMP ("lac-pg-noting-e2e-" + [Guid]::NewGuid().ToString('N'))
}

$dataRoot = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\')
$repoFull = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\')

if ($dataRoot -eq [IO.Path]::GetPathRoot($dataRoot) -or $dataRoot -ieq $repoFull -or $dataRoot.StartsWith($repoFull + '\', [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $dataRoot)) {
    throw 'Use a new disposable directory outside the repository. Existing paths are never overwritten.'
}

if (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -eq $PgPort }).Count) {
    throw "Port $PgPort is occupied. Existing runtimes are never stopped."
}
if (@(Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -eq $ApiPort }).Count) {
    throw "Port $ApiPort is occupied. Existing runtimes are never stopped."
}

# 1. Locate PostgreSQL tools
$init = Get-Command initdb.exe -ErrorAction SilentlyContinue
if (-not $init) {
    $candidates = @(Get-ChildItem -LiteralPath (Join-Path $env:ProgramFiles 'PostgreSQL') -Directory | Where-Object { $_.Name -match '^\d+$' } | Sort-Object { [int]$_.Name } -Descending)
    foreach ($candidate in $candidates) {
        $path = Join-Path $candidate.FullName 'bin\initdb.exe'
        if (Test-Path -LiteralPath $path) {
            $init = Get-Item -LiteralPath $path
            break
        }
    }
}
if (-not $init) { throw 'PostgreSQL tooling not found.' }

$initPath = if ($init.PSObject.Properties['Source']) { $init.Source } else { $init.FullName }
$control = Join-Path (Split-Path -Parent $initPath) 'pg_ctl.exe'

Write-Host "Initializing dedicated test cluster at $dataRoot..."
& $initPath -D $dataRoot -U postgres -A trust --encoding=UTF8 --no-locale
if ($LASTEXITCODE -ne 0) { throw 'Dedicated test cluster initialization failed.' }

Add-Content -LiteralPath (Join-Path $dataRoot 'postgresql.conf') -Value "`nlisten_addresses = '127.0.0.1'`nport = $PgPort`n"

$docDir = Join-Path $dataRoot 'documents'
[IO.Directory]::CreateDirectory($docDir) | Out-Null

$pgStarted = $false
$apiProcess = $null

try {
    Write-Host "Starting PostgreSQL cluster on port $PgPort..."
    & $control start -D $dataRoot -l (Join-Path $dataRoot 'pg.log') -w
    if ($LASTEXITCODE -ne 0) { throw 'Dedicated test cluster did not start.' }
    $pgStarted = $true

    $connectionString = "Host=127.0.0.1;Port=$PgPort;Database=lac_noting_e2e;Username=postgres;Pooling=false"
    $env:LAC_E2E_CONNECTION = $connectionString
    $env:LAC_E2E_DOC_DIR = $docDir
    $env:LAC_REPO_ROOT = $repoRoot

    Write-Host "Seeding test database with officers, desks, Dak, Matter, and PDF document..."
    Push-Location $repoRoot
    try {
        & dotnet test tests/LAC.Tests --filter "FullyQualifiedName~SeedNotingE2ETests"
        if ($LASTEXITCODE -ne 0) { throw 'Database seeding failed.' }
    } finally {
        Pop-Location
    }

    # Refresh wwwroot staging with latest React build
    $wwwroot = Join-Path $repoRoot 'src\LAC.Api\wwwroot'
    $webDist = Join-Path $repoRoot 'src\LAC.Web\dist'
    if (Test-Path -LiteralPath $webDist) {
        Write-Host "Copying latest Web dist to API wwwroot..."
        Copy-Item -Path "$webDist\*" -Destination $wwwroot -Recurse -Force
    }

    # Start LAC.Api on isolated port
    Write-Host "Starting isolated LAC.Api on port $ApiPort..."
    $apiLog = Join-Path $dataRoot 'api.log'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'dotnet'
    $psi.Arguments = "run --project `"$repoRoot\src\LAC.Api`" --no-build"
    $psi.WorkingDirectory = "$repoRoot\src\LAC.Api"
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['ASPNETCORE_URLS'] = "http://127.0.0.1:$ApiPort"
    $psi.EnvironmentVariables['ConnectionStrings__DefaultConnection'] = "Host=127.0.0.1;Port=$PgPort;Database=lac_noting_e2e;Username=postgres;Pooling=true"
    $psi.EnvironmentVariables['Startup__RunDatabaseBootstrap'] = 'false'
    $psi.EnvironmentVariables['BackgroundWorkers__Enabled'] = 'false'
    $psi.EnvironmentVariables['Storage__DocumentRoot'] = $docDir
    $psi.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Development'

    $apiProcess = [System.Diagnostics.Process]::Start($psi)

    # Stream logs to file in background
    $stdoutTask = [System.Threading.Tasks.Task]::Run([Action]{
        $writer = [System.IO.File]::AppendText($apiLog)
        try {
            while (-not $apiProcess.HasExited) {
                $line = $apiProcess.StandardOutput.ReadLine()
                if ($line) { $writer.WriteLine($line); $writer.Flush() }
            }
        } finally { $writer.Dispose() }
    })

    # Wait for API to respond
    Write-Host "Waiting for LAC.Api to be ready on http://127.0.0.1:$ApiPort..."
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        if ($apiProcess.HasExited) {
            throw "LAC.Api terminated unexpectedly with exit code $($apiProcess.ExitCode). Inspect $apiLog"
        }
        try {
            $resp = Invoke-WebRequest -Uri "http://127.0.0.1:$ApiPort/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 2 -ErrorAction SilentlyContinue
            if ($resp.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
    }

    if (-not $ready) {
        throw "LAC.Api failed to become healthy on port $ApiPort within 30 seconds. Inspect $apiLog"
    }

    Write-Host "LAC.Api is ready! Launching genuine browser Playwright E2E test..."
    Push-Location $repoRoot
    try {
        & node src/LAC.Web/tests/noting-real-e2e.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Genuine browser E2E test failed.' }
    } finally {
        Pop-Location
    }

    Write-Host 'GENUINE E2E TEST PASSED 100%'
    Write-Host 'All 7 screenshots created at docs/receipts/matter-noting-workspace/'
} finally {
    if ($apiProcess -and -not $apiProcess.HasExited) {
        Write-Host "Stopping LAC.Api process $($apiProcess.Id)..."
        try { Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue } catch { }
    }
    if ($pgStarted) {
        Write-Host 'Stopping dedicated PostgreSQL cluster...'
        & $control stop -D $dataRoot -m fast -w
    }
}

