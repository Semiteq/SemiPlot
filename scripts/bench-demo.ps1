#Requires -Version 7.0
<#
.SYNOPSIS
    Converges the application bench: a container holding a seeded semiplot_app, and a connection
    file the viewer can read.

.DESCRIPTION
    The canonical recipe for the demo stand. docs/architecture/bench.md points here rather than
    carrying the commands, so the recipe run daily is the recipe documented.

    Every step is skipped when it is already done, so the script is safe to run on every boot and
    after every failure. Three of the steps refuse to repeat themselves anyway: `docker run` fails
    on a name that exists, `CREATE DATABASE` fails on a name that exists, and the seeder refuses an
    archive that already carries rows or day partitions.

    The connection file is the exception: it is rewritten on every run. Its `source_time_zone` must
    name the zone of the machine the demo writer runs on, because the writer writes that machine's
    local wall clock. A stale zone shows as a chart that never advances while the log reads rows
    normally, which is this bench's most expensive failure, so the field is never allowed to age.

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
# The image provisions over a unix socket before the published port opens, so a server that answers
# on TCP has already run `semibase bench` to completion. Polling the published port is therefore the
# whole readiness condition.
$deadline = (Get-Date).AddSeconds(120)
while ($true)
{
    & docker exec --env "PGPASSWORD=$SuperuserPassword" $ContainerName `
        psql --username postgres --dbname postgres --host 127.0.0.1 --command 'SELECT 1' 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0)
    {
        break
    }

    if ((Get-Date) -gt $deadline)
    {
        Write-Host (& docker logs --tail 40 $ContainerName 2>&1 | Out-String)
        throw "The container did not serve on TCP within 120 s. Its last log lines are above."
    }

    Start-Sleep -Milliseconds 500
}
Write-Host '    the published port serves'

Write-Step "Cloning $ProvisionedDatabase into $Database"
$exists = Invoke-Psql 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$Database';"
if ($exists -eq '1')
{
    Write-Skip "$Database already exists"
}
else
{
    Invoke-Psql 'postgres' "CREATE DATABASE $Database TEMPLATE $ProvisionedDatabase;" | Out-Null
    Write-Host "    created $Database"
}

Write-Step 'Seeding the archive'
# The seeder's own refusal is the authority here; this check only keeps the run quiet and its exit
# code clean on the second call.
$rowCount = Invoke-Psql $Database 'SELECT count(*) FROM public.trends;'
if ([int] $rowCount -gt 0)
{
    Write-Skip "already seeded, $rowCount rows"
}
else
{
    $writerConnection = "Host=localhost;Port=$HostPort;Database=$Database;Username=scada_writer;Password=$WriterPassword"
    $adminConnection = "Host=localhost;Port=$HostPort;Database=$Database;Username=postgres;Password=$SuperuserPassword"

    & dotnet run --project (Join-Path $RepositoryRoot 'SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj') -- `
        --connection $writerConnection `
        --admin-connection $adminConnection `
        --end $SeedEnd --days $SeedDays --pens $SeedPens --seed $SeedSeed
    if ($LASTEXITCODE -ne 0)
    {
        throw "The seeder failed with exit code $LASTEXITCODE."
    }
}

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
Write-Host "  archive   $Database on localhost:$HostPort, seeded to $SeedEnd"
Write-Host "  config    $ConfigDirectory"
Write-Host '  next      run the "Live demo" configuration, or "Demo writer" and "Viewer (bench)" apart'
