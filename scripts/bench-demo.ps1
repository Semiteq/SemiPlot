#Requires -Version 7.0
<#
.SYNOPSIS
    Raises the application bench: a container, a seeded archive that reaches the wall clock, and a
    connection file the viewer can read.

.DESCRIPTION
    The canonical recipe for the demo stand. docs/architecture/bench.md points here rather than
    carrying the commands, so the recipe run daily is the recipe documented.

    Every piece of the stand is converged, the archive included, and the script is safe to run as a
    before-launch task of two configurations at once. A named mutex serialises concurrent instances:
    the loser waits and then finds everything already converged, and the mutex is released in a
    finally so a failure does not wedge the next run.

    The image and the container are built once and reused, so the slow half of the recipe is paid
    once per boot rather than once per session.

    semiplot_app is recreated when its newest row is further behind the wall clock than $LiveWithin,
    and kept when it is not. Recreating means terminating the backends, dropping the database,
    cloning semiplot_provisioned and filling the clone up to -SeedEnd.

    Converging the archive is not the cross-session drift the unconditional recreate removed,
    because stale and live are exactly the two states that argument separates. A stale archive is
    the previous session's, and it is recreated, so the extent still starts where the seed puts it
    and never stretches a little further each time. A live archive is one a demo writer is appending
    to at this moment, and keeping it is the loop working rather than drift: dropping it would take
    the database out from under the running writer and leave the stand with no archive at all.

    An explicit -SeedEnd recreates whatever the archive holds. No -Reseed switch stands beside it:
    both intents that need a recreate — the stale-past stand and a pristine reset — are a statement
    of where the fill ends, and a switch meaning "recreate up to the default end" would be a second
    spelling of -SeedEnd with the current instant.

    The connection file is rewritten on both paths. Its source_time_zone must name the zone of the
    machine the demo writer runs on, and a stale zone shows as a chart that never advances while the
    log reads rows normally.

.PARAMETER Down
    Remove the container and the generated connection file instead of converging them.

.PARAMETER SeedEnd
    Naive local instant the fill stops before, as yyyy-MM-ddTHH:mm:ss. Defaults to the script's own
    wall clock, so the fill ends where the demo writer starts. The seeder's --end is exclusive and
    the raw lattice is change-driven, so the newest row lands just under this value rather than on
    it. Stating this parameter recreates the archive whatever it holds, which is what keeps both the
    stale-past stand and a pristine reset reachable. An explicit past value is the stale-past bench,
    and it is the same script with an argument: a chart that opens on the archive extent reaches the
    data, and one that opens on the wall clock does not. That distinction is invisible against an
    archive filled up to now.

.EXAMPLE
    pwsh scripts/bench-demo.ps1
    pwsh scripts/bench-demo.ps1 -SeedEnd 2026-08-01T00:00:00
    pwsh scripts/bench-demo.ps1 -Down
#>
[CmdletBinding()]
param(
    [switch] $Down,
    # Rendered against the invariant culture, never the host's. ':' and '-' in a custom format string
    # are the culture's own separators, so on a host running fi-FI the default would come out as
    # 2026-08-27T21.23.22 — which the seeder's invariant --end parse rejects, on every run.
    [string] $SeedEnd = (Get-Date).ToString('yyyy-MM-ddTHH:mm:ss', [cultureinfo]::InvariantCulture)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$ContainerName = 'semiplot-bench'
# The container's image, port and passwords live in compose.yaml, shared with the Rider configuration
# `Bench container`; the names below repeat what the script needs to talk to it.
$ComposeFile = Join-Path $PSScriptRoot 'compose.yaml'
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

$SeedDays = 1
$SeedPens = 8
$SeedSeed = 1

# The same interval the demo writer runs at, in `.run/Demo writer.run.xml`. Seeding at the seeder's
# own 5 s default and then following at 0.5 s drew a fill ten times sparser than its own live tail,
# which reads on screen as a change in the plant rather than a change in the stand. Holding one
# value here also puts both halves on the same lattice point for point, so the seam carries no step.
$SeedChangeSeconds = 0.5

# How far behind the wall clock the archive's newest row may sit and still count as live. The same
# bound the demo writer applies from the other side, in StaleArchiveGuard.MaximumAge.
#
# Its floor is the writer's tick cadence: at the demo's --follow 1 a running writer keeps max(t) a
# second or two behind the clock, so five minutes is three hundred ticks of margin and no running
# writer is ever read as a stopped one — which matters, because the recreate path terminates the
# backends and drops the database out from under it. Its ceiling is the widest hole a kept archive
# can carry into the next session, and it must still cover the latency between one instance's fill
# ending and the next instance reading max(t), which is a `dotnet run` of the seeder. Five minutes
# against the 793.7 minutes an unchecked run once left is the trade this bound makes.
$LiveWithin = [timespan]::FromMinutes(5)

# Long enough to sit out a cold `docker build` held by the other instance, and short enough that a
# wedged run reports rather than hangs for the session.
$ConvergenceTimeout = [timespan]::FromMinutes(15)

# Stating -SeedEnd states where the fill ends, so it is a recreate whatever the archive holds.
$SeedEndStated = $PSBoundParameters.ContainsKey('SeedEnd')

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

# The archive's newest row, or $null when there is nothing to be stale: no database, no archive table,
# or no rows. Each of those is recreated rather than kept, and each is cheap to recreate.
function Get-ArchiveNewest
{
    $present = Invoke-Psql 'postgres' "SELECT count(*) FROM pg_database WHERE datname = '$Database';"
    if ($present -ne '1')
    {
        return $null
    }

    # to_regclass keeps a database the provisioning never finished out of the read: it answers the
    # empty string exactly as an archive with no rows does.
    $newest = Invoke-Psql $Database @"
SELECT CASE
    WHEN to_regclass('public.trends') IS NULL THEN ''
    ELSE coalesce((SELECT max(t)::text FROM public.trends), '')
END;
"@

    $parsed = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::None
    if (-not [datetime]::TryParse($newest, [cultureinfo]::InvariantCulture, $styles, [ref] $parsed))
    {
        return $null
    }

    return $parsed
}

function Invoke-Down
{
    Write-Step 'Removing the bench'
    if (Test-ContainerExists)
    {
        # rm rather than `compose down` alone: a container an older script created with `docker run`
        # carries no compose labels, and `down` would leave it standing.
        Invoke-Docker rm --force $ContainerName | Out-Null
        Invoke-Docker compose --file $ComposeFile down | Out-Null
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
}

function Invoke-Up
{
    Write-Step 'Checking the container runtime'
    & docker version --format '{{.Server.Version}}' 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw 'No container runtime answered. Start Docker Desktop and run this again.'
    }

    Write-Step "Starting $ContainerName on port $HostPort"
    if (Test-ContainerRunning)
    {
        Write-Skip 'already running'
    }
    else
    {
        # Builds the image when it is missing, starts an existing container or creates one: the same
        # `up` the Rider configuration `Bench container` runs, so either path meets the other's container.
        Invoke-Docker compose --file $ComposeFile up --detach | Out-Null
        Write-Host '    up'
    }

    Wait-ForPort

    $filled = Update-Archive

    Write-ConnectionFile

    Write-Host ''
    Write-Host 'Bench up.' -ForegroundColor Green
    Write-Host "  archive   $Database on localhost:$HostPort, $($filled.RowCount) rows, $($filled.Summary)"
    Write-Host "  newest    $($filled.Newest)"
    Write-Host "  config    $ConfigDirectory"
    Write-Host '  next      the "Live demo" configuration, which runs this script before either child starts'
}

function Wait-ForPort
{
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
}

# Recreates the archive when it is stale, absent or explicitly re-ended, and keeps it when a writer is
# holding its newest row within $LiveWithin of the clock.
function Update-Archive
{
    $newest = Get-ArchiveNewest

    if (-not $SeedEndStated -and $null -ne $newest)
    {
        $age = (Get-Date) - $newest
        if ($age -le $LiveWithin)
        {
            Write-Step "Keeping $Database"
            $behind = [math]::Round($age.TotalSeconds, 1)
            Write-Host "    newest $($newest.ToString('s')), $behind s behind the clock: a writer is keeping it live"
            $kept = Invoke-Psql $Database 'SELECT count(*) FROM public.trends;'

            return @{
                RowCount = $kept
                Newest = $newest.ToString('s')
                Summary = "kept, $behind s behind the clock"
            }
        }
    }

    New-Archive $newest

    $rowCount = Invoke-Psql $Database 'SELECT count(*) FROM public.trends;'
    $extent = Invoke-Psql $Database "SELECT coalesce(max(t)::text, 'empty') FROM public.trends;"

    return @{
        RowCount = $rowCount
        Newest = $extent
        Summary = "filled up to $SeedEnd (exclusive)"
    }
}

function New-Archive($Newest)
{
    Write-Step "Recreating $Database from $ProvisionedDatabase"
    Write-Host "    $(Get-RecreateReason $Newest)"

    # CREATE DATABASE ... TEMPLATE refuses a source another session holds, and semiplot_provisioned is
    # The connection file names a database that is about to stop existing, so it stops being true
    # here and nowhere else. Clearing it on every convergence instead would break the case the mutex
    # exists for: both demo children run this script, the second run keeps the live archive the first
    # one filled, and a second run that cleared the signal would delete it out from under a child the
    # first run already started.
    if (Test-Path $ConnectionFile)
    {
        Remove-Item $ConnectionFile
    }

    # the source of every recreate, so its backends are terminated alongside the target's.
    Invoke-Psql 'postgres' @"
SELECT pg_terminate_backend(pid) FROM pg_stat_activity
WHERE datname IN ('$Database', '$ProvisionedDatabase') AND pid <> pg_backend_pid();
"@ | Out-Null
    Invoke-Psql 'postgres' "DROP DATABASE IF EXISTS $Database;" | Out-Null
    Invoke-Psql 'postgres' "CREATE DATABASE $Database TEMPLATE $ProvisionedDatabase;" | Out-Null

    Write-Step "Seeding $Database up to $SeedEnd"
    # --admin-connection fills semiplot_tags, which scada_writer holds no privilege on.
    # semiplot_provisioned carries no rows and no tag rows, so the clone starts empty and the seeder
    # accepts it.
    $server = "Host=localhost;Port=$HostPort;Database=$Database"
    $writerConnection = "$server;Username=scada_writer;Password=$WriterPassword"
    $adminConnection = "$server;Username=postgres;Password=$SuperuserPassword"
    $seederProject = Join-Path $RepositoryRoot 'SemiPlot/SemiPlot.Tools.ArchiveSeeder'
    $seederProject = Join-Path $seederProject 'SemiPlot.Tools.ArchiveSeeder.csproj'

    # Out-Host, not the bare pipeline: Update-Archive's own output is captured by its caller, so the
    # seeder's lines have to reach the console beside that value rather than in front of it.
    $seedStarted = Get-Date
    & dotnet run --project $seederProject -- `
        --connection $writerConnection `
        --admin-connection $adminConnection `
        --end $SeedEnd --days $SeedDays --pens $SeedPens --seed $SeedSeed `
        --change-seconds $SeedChangeSeconds | Out-Host
    if ($LASTEXITCODE -ne 0)
    {
        throw "The seeder failed with exit code $LASTEXITCODE."
    }

    $seedSeconds = [math]::Round(((Get-Date) - $seedStarted).TotalSeconds, 1)
    Write-Host "    seeded in $seedSeconds s"
}

function Get-RecreateReason($Newest)
{
    if ($SeedEndStated)
    {
        return "-SeedEnd states where the fill ends, so the archive is recreated whatever it holds"
    }

    if ($null -eq $Newest)
    {
        return 'no archive to keep: the database, the archive table or its rows are absent'
    }

    $behind = [math]::Round(((Get-Date) - $Newest).TotalMinutes, 1)
    $bound = $LiveWithin.TotalMinutes
    return "newest $($Newest.ToString('s')) is $behind min behind the clock, past the $bound min bound"
}

function Write-ConnectionFile
{
    Write-Step 'Writing the connection file'
    # Rewritten every run, never patched. TimeZoneInfo.FindSystemTimeZoneById resolves this machine's
    # own identifier on this machine, so the Windows id goes in verbatim and needs no IANA conversion.
    $zone = (Get-TimeZone).Id
    New-Item -ItemType Directory -Force -Path $ConfigDirectory | Out-Null
    @"
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
}

# One mutex over the whole convergence, so two before-launch instances started by one compound
# serialise instead of racing the same `docker run` and the same DROP DATABASE. The loser waits and
# then finds an archive the winner already filled, which the freshness check above keeps.
$convergence = [System.Threading.Mutex]::new($false, 'Global\semiplot-bench')
$held = $false

try
{
    try
    {
        $held = $convergence.WaitOne([timespan]::Zero)
        if (-not $held)
        {
            Write-Step 'Waiting for the other instance to finish converging the bench'
            $held = $convergence.WaitOne($ConvergenceTimeout)
        }
    }
    catch [System.Threading.AbandonedMutexException]
    {
        # A previous holder died without releasing. The wait still succeeded and this process now owns
        # the mutex, so the convergence goes ahead and re-establishes whatever that run left half done.
        $held = $true
        Write-Skip 'the previous holder did not release the mutex; converging over what it left'
    }

    if (-not $held)
    {
        throw "Another instance held 'Global\semiplot-bench' for " `
            + "$($ConvergenceTimeout.TotalMinutes) minutes. Check for a stuck bench-demo run."
    }

    if ($Down)
    {
        Invoke-Down
    }
    else
    {
        Invoke-Up
    }
}
finally
{
    if ($held)
    {
        $convergence.ReleaseMutex()
    }

    $convergence.Dispose()
}
