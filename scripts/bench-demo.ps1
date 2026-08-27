#Requires -Version 7.0
<#
.SYNOPSIS
    Raises the application bench: a container, a pristine seeded archive, and a connection file the
    viewer can read.

.DESCRIPTION
    The canonical recipe for the demo stand. docs/architecture/bench.md points here rather than
    carrying the commands, so the recipe run daily is the recipe documented.

    Two lifetimes, and the split is the point.

    The image, the container and semiplot_seeded are converged: each is built once and reused, so
    the expensive half of the recipe is paid once per boot rather than once per session.

    semiplot_app and the connection file are recreated every run. The demo writer appends to the
    archive, so a converged database would carry the previous session's live rows and its extent
    would stand a little further from its seed each time — a bench that drifts is a bench whose
    reading cannot be trusted. A TEMPLATE clone copies files rather than replaying the seeder, so a
    pristine archive costs seconds. The connection file is rewritten for a related reason: its
    source_time_zone must name the zone of the machine the demo writer runs on, and a stale zone
    shows as a chart that never advances while the log reads rows normally.

.PARAMETER Down
    Remove the container and the generated connection file instead of converging them.

.EXAMPLE
    pwsh scripts/bench-demo.ps1
    pwsh scripts/bench-demo.ps1 -Down
#>
[CmdletBinding()]
param(
    [switch] $Down
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$ContainerName = 'semiplot-bench'
$ImageTag = 'semiplot-bench:manual'
$BenchContext = Join-Path $RepositoryRoot 'SemiPlot/SemiPlot.Tests.Data/bench'
$ConfigDirectory = Join-Path $RepositoryRoot 'SemiPlot/Artifacts/bench-config'
$ConnectionFile = Join-Path $ConfigDirectory 'archive-connection.yaml'
$HostPort = 55432
$Database = 'semiplot_app'
$SeededTemplate = 'semiplot_seeded'
$ProvisionedDatabase = 'semiplot_provisioned'

# The container's roles carry the fixture's own fixed passwords, which are public constants in
# SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs. They reach nothing but a throwaway
# container on the loopback interface.
$SuperuserPassword = 'semibase-container-superuser'
$WriterPassword = 'semibase-container-writer'
$ReaderPassword = 'semibase-container-reader'

# The archive is seeded to a span well in the past on purpose: a chart that opens on the archive
# extent reaches the data, and one that opens on the wall clock does not. The distinction is
# invisible against an archive seeded up to now.
$SeedEnd = '2026-08-01T00:00:00'
$SeedDays = 1
$SeedPens = 8
$SeedSeed = 1

function Write-Step([string] $Message)
{
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-Skip([string] $Message)
{
    Write-Host "    $Message" -ForegroundColor DarkGray
}

function Invoke-Docker
{
    $output = & docker @args 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "docker $($args -join ' ') failed with exit code $LASTEXITCODE`n$output"
    }

    return $output
}

function Test-ContainerExists
{
    $names = & docker ps --all --filter "name=^/$ContainerName$" --format '{{.Names}}' 2>$null
    return $LASTEXITCODE -eq 0 -and $names -contains $ContainerName
}

function Test-ContainerRunning
{
    $names = & docker ps --filter "name=^/$ContainerName$" --format '{{.Names}}' 2>$null
    return $LASTEXITCODE -eq 0 -and $names -contains $ContainerName
}

function Invoke-Psql([string] $TargetDatabase, [string] $Command)
{
    $result = & docker exec --env "PGPASSWORD=$SuperuserPassword" $ContainerName `
        psql --username postgres --dbname $TargetDatabase --tuples-only --no-align --command $Command 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "psql against '$TargetDatabase' failed with exit code $LASTEXITCODE`n$result"
    }

    return ($result | Out-String).Trim()
}

if ($Down)
{
    Write-Step 'Removing the bench'
    if (Test-ContainerExists)
    {
        Invoke-Docker rm --force $ContainerName | Out-Null
        Write-Host "    removed container $ContainerName"
    }
    else
    {
        Write-Skip "no container named $ContainerName"
    }

    if (Test-Path $ConnectionFile)
    {
        Remove-Item $ConnectionFile
        Write-Host "    removed $ConnectionFile"
    }
    else
    {
        Write-Skip 'no connection file'
    }

    Write-Host 'Bench down.' -ForegroundColor Green
    return
}

Write-Step 'Checking the container runtime'
& docker version --format '{{.Server.Version}}' 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0)
{
    throw 'No container runtime answered. Start Docker Desktop and run this again.'
}

Write-Step "Building $ImageTag"
Invoke-Docker build --tag $ImageTag $BenchContext | Out-Null

Write-Step "Starting $ContainerName on port $HostPort"
if (Test-ContainerRunning)
{
    Write-Skip 'already running'
}
elseif (Test-ContainerExists)
{
    Invoke-Docker start $ContainerName | Out-Null
    Write-Host '    started the existing container'
}
else
{
    Invoke-Docker run --detach --name $ContainerName --publish "${HostPort}:5432" `
        --env "POSTGRES_PASSWORD=$SuperuserPassword" `
        --env "SEMIBASE_WRITER_PASSWORD=$WriterPassword" `
        --env "SEMIBASE_READER_PASSWORD=$ReaderPassword" `
        --env "SEMIPLOT_PROVISIONED_DATABASE=$ProvisionedDatabase" `
        $ImageTag | Out-Null
    Write-Host '    created a new container'
}

Write-Step 'Waiting for the provisioning to finish'
# The image provisions over a unix socket before the published port opens, so a server reachable on
# TCP has already run `semibase bench` to completion.
#
# The probe goes out to host.docker.internal and back through the published port rather than to
# 127.0.0.1 inside the container, because that is the path the seeder and the viewer take. An
# in-container probe passes while the port mapping is still settling, which shows up as a seeder
# that fails once on a freshly created container and succeeds on the next run.
$deadline = (Get-Date).AddSeconds(120)
while ($true)
{
    & docker exec --env "PGPASSWORD=$SuperuserPassword" $ContainerName `
        psql --username postgres --dbname postgres `
        --host host.docker.internal --port $HostPort --command 'SELECT 1' 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0)
    {
        break
    }

    if ((Get-Date) -gt $deadline)
    {
        Write-Host (& docker logs --tail 40 $ContainerName 2>&1 | Out-String)
        throw "Port $HostPort did not serve within 120 s. The container's last log lines are above."
    }

    Start-Sleep -Milliseconds 500
}
Write-Host "    port $HostPort serves"

Write-Step "Seeding $SeededTemplate"
# Seeded once and never written to again, so the expensive half of the recipe is paid once per
# container. The demo writer appends to the clone below, never here.
$exists = Invoke-Psql 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$SeededTemplate';"
if ($exists -eq '1')
{
    $rowCount = Invoke-Psql $SeededTemplate 'SELECT count(*) FROM public.trends;'
    Write-Skip "already seeded, $rowCount rows"
}
else
{
    Invoke-Psql 'postgres' "CREATE DATABASE $SeededTemplate TEMPLATE $ProvisionedDatabase;" | Out-Null

    $writerConnection = "Host=localhost;Port=$HostPort;Database=$SeededTemplate;Username=scada_writer;Password=$WriterPassword"
    $adminConnection = "Host=localhost;Port=$HostPort;Database=$SeededTemplate;Username=postgres;Password=$SuperuserPassword"

    & dotnet run --project (Join-Path $RepositoryRoot 'SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj') -- `
        --connection $writerConnection `
        --admin-connection $adminConnection `
        --end $SeedEnd --days $SeedDays --pens $SeedPens --seed $SeedSeed
    if ($LASTEXITCODE -ne 0)
    {
        throw "The seeder failed with exit code $LASTEXITCODE."
    }
}

Write-Step "Recreating $Database from $SeededTemplate"
# Dropped and re-cloned on every run, never converged. The demo writer appends to this database and
# the viewer's own poll leaves nothing behind, but the appended rows do survive the session: keeping
# them would stretch the archive's extent a little further from its seed every time, which is the
# one property this bench must not lose. A TEMPLATE clone copies files rather than replaying the
# seeder, so a pristine archive costs seconds.
Invoke-Psql 'postgres' @"
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
WHERE datname IN ('$Database', '$SeededTemplate') AND pid <> pg_backend_pid();
"@ | Out-Null
Invoke-Psql 'postgres' "DROP DATABASE IF EXISTS $Database;" | Out-Null
Invoke-Psql 'postgres' "CREATE DATABASE $Database TEMPLATE $SeededTemplate;" | Out-Null
$rowCount = Invoke-Psql $Database 'SELECT count(*) FROM public.trends;'
$extent = Invoke-Psql $Database "SELECT coalesce(max(t)::text, 'empty') FROM public.trends;"
Write-Host "    $rowCount rows, newest $extent"

Write-Step 'Writing the connection file'
# Rewritten every run, never patched. TimeZoneInfo.FindSystemTimeZoneById resolves this machine's
# own identifier on this machine, so the Windows id goes in verbatim and needs no IANA conversion.
$zone = (Get-TimeZone).Id
New-Item -ItemType Directory -Force -Path $ConfigDirectory | Out-Null
@"
connection_file_version: "1.0"
host: localhost
port: $HostPort
database: $Database
user: semiplot_reader
password: "$ReaderPassword"
source_time_zone: $zone
poll_interval_ms: 1000
schema: public
"@ | Set-Content -Path $ConnectionFile -Encoding utf8NoBOM
Write-Host "    $ConnectionFile, source_time_zone $zone"

Write-Host ''
Write-Host 'Bench up.' -ForegroundColor Green
Write-Host "  archive   $Database on localhost:$HostPort, a fresh clone seeded to $SeedEnd"
Write-Host "  config    $ConfigDirectory"
Write-Host '  next      run the "Live demo" configuration, or "Demo writer" and "Viewer (bench)" apart'
