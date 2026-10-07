#requires -Version 7
<#
.SYNOPSIS
  Starts the Azure SQL Database container (Private Preview) for local AQR development and wires the app to it.

.DESCRIPTION
  Uses the real Azure SQL Database engine in a container (SERVERPROPERTY('EngineEdition') = 5), per
  https://devblogs.microsoft.com/azure-sql/build-locally-ship-to-cloud-for-0-azure-sql-for-modern-app-developers/
  One-time prerequisites (you): Private Preview sign-up (https://aka.ms/sqldbcontainerpreview-signup), a container
  runtime (wslc / docker / podman), and a registry login. See docs/LOCAL_DEV.md.
  The SA password is generated here and stored only in .NET user-secrets for src/Aqr.Web (never in the repo).

.PARAMETER Runtime   wslc | docker | podman (auto-detected when omitted)
.PARAMETER Port      Host port (default 1433)
.PARAMETER Database  Database name (default aqr)
.PARAMETER Mock      Keep Aqr:UseMock=true (synthetic data). Default true.
#>
param(
    [ValidateSet('wslc', 'docker', 'podman')] [string] $Runtime,
    [int] $Port = 1433,
    [string] $Database = 'aqr',
    [bool] $Mock = $true
)
$ErrorActionPreference = 'Stop'
$registry = 'sqldbpreview-dpgaeqhmgphzd4bk.azurecr.io'
$image = "$registry/azure-sql/db-dev:latest"
$name = 'aqr-sqldb'
if ($Database -notmatch '^[A-Za-z][A-Za-z0-9_]{0,60}$') { throw 'Database name must be alphanumeric/underscore.' }

if (-not $Runtime) {
    $Runtime = @('wslc', 'docker', 'podman') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
    if (-not $Runtime) { throw 'No container runtime found. Install WSL (wsl --install, then reboot) for wslc, or Docker/Podman. See docs/LOCAL_DEV.md.' }
}
Write-Host "Using container runtime: $Runtime"

# Generate a password that meets the SQL complexity policy; keep it only in user-secrets.
$web = Join-Path $PSScriptRoot '..' 'src' 'Aqr.Web'
$existing = (dotnet user-secrets list --project $web 2>$null) | Where-Object { $_ -like 'LocalSql:SaPassword = *' } | Select-Object -First 1
$password = if ($existing) { $existing.Substring('LocalSql:SaPassword = '.Length) } else {
    $chars = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    'Aq1!' + -join (1..20 | ForEach-Object { $chars[[System.Security.Cryptography.RandomNumberGenerator]::GetInt32($chars.Length)] })
}

& $Runtime rm -f $name 2>$null | Out-Null
& $Runtime pull $image
if ($LASTEXITCODE -ne 0) { throw "Pull failed. Sign in to $registry first (docs/LOCAL_DEV.md, step 3)." }
& $Runtime run -d --name $name -e 'ACCEPT_EULA=Y' -e "MSSQL_SA_PASSWORD=$password" -p "${Port}:1433" $image | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Container failed to start. Check: $Runtime logs $name" }

Write-Host 'Waiting for the engine and provisioning the database...'
$ready = $false
foreach ($i in 1..60) {
    & $Runtime exec $name /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $password -C -b -l 2 `
        -Q "IF DB_ID('$Database') IS NULL CREATE DATABASE [$Database];" 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw "Engine didn't become ready. Check: $Runtime logs $name" }

$edition = & $Runtime exec $name /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $password -C -h -1 -d $Database `
    -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('EngineEdition') AS int);"
if (($edition | Out-String).Trim() -ne '5') { throw "Unexpected engine edition '$edition' (expected 5 = Azure SQL Database). Is this the Azure SQL Database container image?" }

$conn = "Server=localhost,$Port;Database=$Database;User Id=sa;Password=$password;Encrypt=True;TrustServerCertificate=True;"
dotnet user-secrets set 'LocalSql:SaPassword' $password --project $web | Out-Null
dotnet user-secrets set 'ConnectionStrings:AqrDb' $conn --project $web | Out-Null
dotnet user-secrets set 'Aqr:UseMock' ($Mock.ToString().ToLowerInvariant()) --project $web | Out-Null

Write-Host "Azure SQL Database engine ready on localhost,$Port (EngineEdition 5). Connection string saved to user-secrets."
Write-Host 'Run the app:  dotnet run --project src/Aqr.Web'
Write-Host 'SQL tests:    set AQR_TEST_SQL to the same connection string without Database= (see docs/LOCAL_DEV.md), then dotnet test'
