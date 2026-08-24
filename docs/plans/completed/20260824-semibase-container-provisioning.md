# Provision the bench from a container that carries its own provisioner

## Overview

The gated suite resolves the `semibase` binary from `SEMIBASE_EXE` or `PATH`
(`SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseBinary.cs`), so on a developer machine whichever
build happens to be installed is the one that provisions.

SemiBase v0.3.0 removes the reason for that. It publishes `ghcr.io/semiteq/semibase`, it provisions
over a unix socket, and — the change that reshapes this slice — **it creates `public.trends` itself**,
from the `scada_writer` role, in both of its two commands.

That last fact makes two previously separate pieces of work one. A Dockerfile here layers the
provisioner onto the PostgreSQL image with a script in `/docker-entrypoint-initdb.d/`; the entrypoint
runs it before the mapped port accepts anything. But the provisioning creates the archive table, and
`ArchiveWriter` **refuses** an archive that already exists (`ArchiveWriter.cs:50-54`). Adopting the
image without inverting that refusal is not a smaller change — it is a bench that does not start.

**The shape that makes it tractable: the image provisions one fixed database.** It cannot know the
seeded template's name, which is a per-build hash (`ArchiveTemplate.cs:111-132`). So the image
provisions `semiplot_provisioned`, and the fixture clones that for everything it needs — the seeded
template, and the "provisioned but carrying no archive" databases two read tests require.
`CREATE DATABASE ... TEMPLATE` copies table ownership, `relacl` and `pg_default_acl`; the
database-level `CONNECT` is not copied but `PUBLIC`'s default covers it, which is already why today's
clones are readable by `semiplot_reader`.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice semibase-container-provisioning

### What SemiBase ships now

Verified against `SemiBase@dad5f3d` (v0.3.0):

- Two commands, `site` and `bench` (`cmd/semibase/main.go:63-66`). `bench` creates the database, both
  roles, the grants, the default-privileges chain, `semiplot_tags`, and `public.trends` with
  `tpdefault`, and applies no `ALTER SYSTEM` tuning (`internal/provision/provision.go:58-63`,
  `:154-232`, `sql/trends.sql:19-31`). `site` additionally applies memory constants sized to an 8 GB
  site machine, which a container must never receive.
- `create`, `config`, `verify` and `all` no longer exist. `SemibaseProvisioner.CreateCommand` is
  `"create"` (`SemibaseProvisioner.cs:17`) and must become `"bench"`.
- The table is created over `SET ROLE scada_writer` on the superuser connection
  (`provision.go:245-288`), so no writer login and no `pg_hba` entry is needed.
- `create` also sets `statement_timeout = '30s'` and `idle_in_transaction_session_timeout = '60s'` on
  `semiplot_reader` (`provision.go:193-202`) — relevant because `StatementTimeoutReadTests` overrides
  them per database.
- **Both commands end with a real reader `SELECT`, not a catalog bit** (`provision.go:379-427`), and
  exit non-zero when it fails. When a reader password is present they additionally perform a real
  TCP login as `semiplot_reader` (`checkReaderLogin`, `:468-490`), skipped only on a socket host.
- The image is `FROM scratch` carrying only `/semibase` (`Dockerfile:15-21`), so copying the binary
  out is the only supported use.
- SemiBase's own CI exercises exactly this Dockerfile-plus-init-script shape at
  `.github/workflows/ci.yml:78-125` — the reference implementation for Task 1, not a blank page.
- The socket directory is `/var/run/postgresql`; init scripts run as the image's `postgres` user
  under local `trust`, so no superuser password is needed there.
- **Fail-closed**: `set -e` in the init script aborts the entrypoint, the container exits 1, and the
  published port never opens.
- **Init scripts run only on an empty `PGDATA`.** A reused volume skips them entirely.

### What this repository has today

- `SemibaseBinary.cs` (69 lines) resolves the executable from `SEMIBASE_EXE` then `PATH`.
  `PostgresContainerFixture.cs:75` resolves it **before** choosing a path, and
  `PostgresServer.SemibaseExecutable` is a non-nullable positional member (`PostgresServer.cs:9`).
  Both must change or Evidence 4 cannot pass.
- `PostgresContainerFixtureTests.cs:48-60` asserts the resolved binary reports
  `SemibaseBinary.PinnedVersion` — `"v0.1.0"` (`SemibaseBinary.cs:13`). After the switch that test is
  self-contradictory: it demands a v0.1.0 binary on a path that must issue `bench`, which exists only
  from v0.3.0.
- `ArchiveWriter.ArchiveExistsCommand` (`:21`) has **four** call sites, not three:
  `ArchiveWriter.cs:92`, `ArchiveTemplate.cs:98`, `ArchiveWriterTransactionTests.cs:70` and
  `ArchiveDatabaseTests.cs:44` (inline through `ScalarAsync<bool>`).
- `ArchiveWriter.ReadSchemaScript()` has **three** consumers, not one: `ArchiveWriter.cs:61`,
  `ArchiveWriter.cs:25` and **`ArchiveTemplate.cs:118`**, which feeds the schema into the template
  name digest. Deleting the resource without touching `ArchiveTemplate` breaks the build.
- `sql/semiplot_dev.sql` is at the repository root, referenced as `..\..\sql\semiplot_dev.sql`
  (`SemiPlot.Tools.ArchiveSeeder.csproj:15-16`), and pinned by `SchemaResourceTests` (11 cases).
- `PostgresExtentReadTests.cs:109-133` and `PostgresHistoryReadTests.cs:299-325` each create an empty
  database, call `SemibaseProvisioner.CreateAsync` directly, and assert "provisioned,
  `semiplot_tags` present, `trends` absent". Under `bench` that state does not exist, and both spawn
  the machine binary on the container path — so **Evidence 4 fails at these two tests** regardless of
  what the fixture does.
- `ArchiveWriterTransactionTests.cs:30` writes into a `template0` database precisely because it
  carries none of SemiBase's grants (comment at `:9-10`). Once an absent `trends` is a failure, the
  writer refuses before the transaction under test starts.
- `SemiPlot.Tests.Data.csproj:22` copies `Fixtures\real-archive-rows.csv` to the output. A test
  assembly runs from its own output directory with no path to the repository — the reason
  `ArchiveWriter.cs:14-16` gives for embedding the SQL — so a Dockerfile in the source tree is not
  reachable at run time unless it is copied the same way.

### Out of scope but noticed, so it is not read as unnoticed

`StartupFailureMapper.cs:125,135` tells the operator to "Run 'semibase create'", a command v0.3.0
removed, and `:120-133` asserts that a missing `trends` is the SCADA's own table which SemiBase never
creates — which v0.3.0 makes false. `StartupFailureMapperTests.cs:103,114,124` pin that literal, and
`MissingRelationProbe.cs:69-70` carries the same reasoning. All of it belongs with
`missing-relation-probe-removal`, which owns that mapper's cold path. This slice leaves it and says
so; Evidence 2 is written accordingly.

### The two axes this slice must not confuse

| | Container path | `SEMIPLOT_TEST_PG` path |
| --- | --- | --- |
| Who provisions | the init script inside the image | the fixture, spawning the binary |
| Which command | `bench` | `bench` |
| How it connects | unix socket, local trust | TCP, superuser password |
| `SEMIBASE_EXE` | not used | **still required** |

An init script provisions only the fresh cluster inside its own container, so the second path cannot
use it. That is why the process-spawning code survives while the `PATH` search dies.

That path also gets **stricter**, not merely renamed: v0.1.0's `create` did not run the access
checks — they were the separate `verify` — while v0.3.0's `bench` always does, including the reader
TCP login. An external server whose `pg_hba.conf` does not admit `semiplot_reader` now fails the
fixture where it previously passed.

### Baseline

Measured 2026-08-24 at `e4ff28f`: `SemiPlot.Tests` 370 passed, 0 skipped, 0 failed.
`SemiPlot.Tests.Data` 382 passed, 0 skipped, 0 failed, 11 s (measured in Task 1, container path,
`semibase v0.1.0` on `PATH`).

## Development Approach

- **Testing approach**: Regular. The existing gated suite is the guard; this slice changes how it
  gets a database, not what it asserts.
- Complete each task fully before moving to the next.
- **All tests must pass before starting the next task**, which is why the image adoption, the
  seeder's inversion and the four affected test files land in one task: between them the bench does
  not work.

## Testing Strategy

- **Gated integration tests**: unchanged in content, entirely changed in provenance.
- **`SchemaResourceTests`** retires with the resource it pins.
- **`TheResolvedBinaryReportsThePinnedVersion`** retires with the pin it asserts.
- **Two read tests change how they reach their state** rather than what they assert: they clone the
  provisioned source and drop `trends`, using the idiom `ArchiveReadSupport.cs:12` already has for
  `DropCatalogCommand`.
- **No shape assertion replaces the retiring one.** A second transcription of the vendor DDL on this
  side is the drift the move exists to kill.

## Acceptance Evidence

**Evidence 1 — the solution builds.**
```powershell
dotnet build SemiPlot.slnx -c Release
```
Exit 0, zero warnings introduced.

**Evidence 2 — the UI suite is unchanged.**
```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
```
370 passed, 0 skipped, 0 failed. The stale `semibase create` advice in `StartupFailureMapper` is
deliberately left for `missing-relation-probe-removal`, so this count must not move.

**Evidence 3 — the gated suite passes, provisioned by the image.**
```powershell
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
```
Zero failures, zero skips. The count drops by `SchemaResourceTests` (11 cases) and
`TheResolvedBinaryReportsThePinnedVersion` (1). The two read tests change shape, not count.

**Evidence 4 — no binary is resolved from the machine on the container path.** With `SEMIBASE_EXE`
unset and no `semibase` anywhere on `PATH`, Evidence 3 still passes. **This is the point of the slice
and the one check that proves it.**

**Evidence 5 — the container's own wait strategy names the provisioned table.** Rather than
observing the ordering once and trusting it, the fixture asserts it at start time: the wait strategy
runs a query against `public.trends` inside the container, so a container whose provisioning did not
complete never becomes ready. Confirm the strategy is in place and that a container reaches ready
only after `semibase bench` has logged completion.

**Evidence 6 — a broken provisioning fails the run rather than yielding an empty database.** The
lever is omitting `SEMIBASE_WRITER_PASSWORD` on a fresh cluster: `ensureRole` fails with "role
scada_writer does not exist and no password was given to create it"
(`SemiBase/internal/provision/provision.go:342-344`), `set -e` aborts the entrypoint, the container
exits 1. A wrong `--database` does **not** work as a lever — `bench` simply creates that database and
exits 0. Record the message the fixture surfaces.

**Evidence 7 — the `SEMIPLOT_TEST_PG` path still works.** Point it at a server the fixture did not
create, with `SEMIBASE_EXE` naming a v0.3.0 binary, and confirm the suite provisions it by spawning
the binary. Note that a failure here may mean the server's `pg_hba.conf` does not admit
`semiplot_reader`, which `bench` now checks and `create` did not. Report whether you could run this;
if no such server is available, say so rather than claiming it.

**Evidence 8 — formatting and encoding.**
```powershell
dotnet format SemiPlot.slnx --verify-no-changes
```
Exit 0. Every tracked `.cs` file still begins `ef bb bf`; no tracked `.md` gains a BOM.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update this plan if implementation deviates from the original scope

## Solution Overview

Task 1 adds the image and proves it standalone. Task 2 is the switch: the fixture takes its database
from the image, the seeder's precondition inverts, and every consumer that assumed otherwise moves
with it — one task, because between any two of those edits the bench is red. Task 3 removes what the
switch orphans. Task 4 updates CI. Task 5 verifies, Task 6 documents.

## Technical Details

**Why one fixed database rather than parameterising the image.** The seeded template's name is a
per-build hash and the init script runs at container start, so the image would have to be told the
name through an environment variable read at exactly the right moment. Provisioning one fixed
database instead lets the fixture derive everything by cloning, which it already does for per-class
databases, and keeps the image ignorant of this repository's naming.

**Why the seeder's refusal inverts rather than relaxes.** The refusal exists so a half-filled archive
is never read as a whole one. That hazard does not go away; its signature changes. The table existing
stops being evidence of a previous run, because provisioning creates it. Rows and day partitions
become the evidence instead — and they are the more direct signal, being what a partial run leaves.

**The transaction narrows and still holds.** Today schema, partitions and COPY are one transaction.
After this slice the schema is outside it, created by provisioning and empty by construction;
partitions and rows stay inside, so a rolled-back run leaves an empty table and nothing to clean up.

**The template name digest loses a term.** `ComputeName` mixes the seeder's module version, the
schema script and the slice options. The schema is no longer this repository's, so it leaves the
digest; the module version and the options still discriminate every bench this repository builds.

## What Goes Where

- **Implementation Steps** — the image, the fixture, the seeder, the tests that assumed the old
  provisioning, the CI workflow, and the documents describing how the bench is provisioned.
- **Post-Completion** — nothing manual; every acceptance item is a command.

## Implementation Steps

### Task 1: Add the image and prove it in isolation

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/bench/Dockerfile`
- Create: `SemiPlot/SemiPlot.Tests.Data/bench/provision.sh`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`

- [x] capture the baseline: run Evidence 3 as the tree stands and record the count here
- [x] read `SemiBase/.github/workflows/ci.yml:78-125` first — it is the reference implementation of
      this exact shape, already proven on a runner
- [x] the Dockerfile takes the base image as `ARG BASE_IMAGE` so `SEMIPLOT_PG_IMAGE` keeps meaning
      what its consumers think, copies `/semibase` out of `ghcr.io/semiteq/semibase:latest`, and
      places the init script in `/docker-entrypoint-initdb.d/` with `COPY --chmod=0755` — the mode
      will not survive a Windows checkout, and the entrypoint **sources** a non-executable `.sh`
      rather than running it
- [x] the init script runs `semibase bench --host /var/run/postgresql --database semiplot_provisioned`
      with `set -e`, taking the two role passwords from the environment. Do not pass
      `--expected-major`: it would contradict a configurable base image, and SemiBase's own floor of
      14 applies anyway
- [x] copy the `bench/` directory to the output directory, beside the existing
      `Fixtures\real-archive-rows.csv` item — a test assembly cannot reach the source tree
- [x] decide and state how the image is built: in-process from the copied context, or pre-built by a
      CI step named through an environment variable
- [x] build it by hand, run a container, and record here that `semibase bench` completed in
      `docker logs` and that `semiplot_reader` can `SELECT` from `public.trends` over the published
      port
- [x] wire nothing in — this task ends with a proven image and an unchanged suite
- [x] run Evidence 3 and confirm the count is unchanged

**Baseline (Evidence 3, tree unchanged, `semibase v0.1.0` on `PATH`):** 382 passed, 0 skipped,
0 failed, 11 s. Re-run after the two new files and the `csproj` item: **382 passed, 0 skipped,
0 failed, 11 s** — unchanged, as this task requires.

**Decision — the fixture builds the image in-process (Testcontainers `ImageFromDockerfileBuilder`)
from the `bench/` directory copied to the output directory.** Task 2 wires it that way. Three
reasons:

1. **One path, not two.** A pre-built image named through an environment variable works only where a
   CI step ran first. A developer machine still needs the in-process build as a fallback, so that
   option is *both* mechanisms plus the branch between them.
2. **It is not a rebuild per run.** The context is two files and every layer is content-addressed, so
   the second and every later build is a cache lookup. Measured on this host: **1.4 s** for a cached
   rebuild, against ~11 s for the whole gated suite. A cold build after a `docker system prune` pays
   the base-image pull once, which the pre-built option pays too.
3. **`SEMIPLOT_PG_IMAGE` keeps its meaning.** It becomes `--build-arg BASE_IMAGE`, so the variable
   still selects the PostgreSQL version. A pre-built image would have to be rebuilt out-of-band to
   honour it, or silently ignore it.

**Manual proof of the image** (built as `semiplot-bench:manual` from
`SemiPlot/SemiPlot.Tests.Data/bench`, `--build-arg BASE_IMAGE=postgres:17-alpine`, published on
55432):

- `docker logs` shows the entrypoint running `/docker-entrypoint-initdb.d/10-semibase.sh`, then
  `[ OK ] public.trends created as scada_writer, partitioned by t, with the tpdefault partition`,
  `[ OK ] semiplot_reader reads public.trends`, and
  `Done: bench completed against /var/run/postgresql/.s.PGSQL.5432 (semiplot_provisioned).`
  The reader TCP login is skipped with `[NOTE] socket host`, exactly as `provision.go:468-490` says.
- `semiplot_reader` over the **published** port:
  `psql -h host.docker.internal -p 55432 -U semiplot_reader -d semiplot_provisioned
  -tAc "select current_user, count(*) from public.trends"` → `semiplot_reader|0`.
- Fail-closed, checked early because Evidence 6 rests on it: the same image run **without**
  `SEMIBASE_WRITER_PASSWORD` logs
  `error: role scada_writer does not exist and no password was given to create it` and the container
  exits **1**; no port ever serves.

### Task 2: Switch the fixture to the image and move every consumer with it

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresServer.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseProvisioner.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabase.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveReadSupport.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveWriterTransactionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabaseTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresExtentReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresHistoryReadTests.cs`

- [x] start the container from the bench image, with a wait strategy that queries `public.trends`
      inside the container, so a container whose provisioning did not complete never becomes ready
      (Evidence 5)
- [x] the image provisions `semiplot_provisioned`; `ArchiveTemplate.BuildAsync` creates the seeded
      template as a clone of it rather than calling the provisioner
- [x] add a way to clone the provisioned source for a database that must carry the grants and the
      empty archive but no seeded rows — the two read tests and the transaction test all need it
- [x] move the binary resolution out of `PostgresContainerFixture.InitializeAsync:75` into the
      `SEMIPLOT_TEST_PG` branch, and make `PostgresServer.SemibaseExecutable` optional. Until both
      change, Evidence 4 cannot pass
- [x] change `SemibaseProvisioner.CreateCommand` from `"create"` to `"bench"`; the type stays for the
      `SEMIPLOT_TEST_PG` path
- [x] invert `ArchiveWriter`'s precondition: an existing empty `public.trends` is expected; an absent
      one is a failure naming the provisioning that did not run; the refusal keys on rows and day
      partitions. Narrow the transaction to partitions and rows and correct the comment at `:56`
- [x] revisit **all four** `ArchiveExistsCommand` call sites separately — `ArchiveWriter.cs:92`,
      `ArchiveTemplate.cs:98`, `ArchiveWriterTransactionTests.cs:70`, `ArchiveDatabaseTests.cs:44`.
      They share a constant, not a meaning; missing one is this task's main risk
- [x] `ArchiveWriterTransactionTests` currently writes into a `template0` database because it carries
      no grants; it must now write into a provisioned clone, and its invariant becomes "leaves no rows
      and no day partitions behind"
- [x] `PostgresExtentReadTests` and `PostgresHistoryReadTests` each build their "provisioned but no
      archive" state by calling the provisioner directly. Rebuild it by cloning and dropping the
      table — add `DropTrendsCommand` beside `ArchiveReadSupport.cs:12`'s `DropCatalogCommand` and
      issue it as the owner
- [x] run Evidence 1, 3, 5 and 6

**What the four `ArchiveExistsCommand` call sites became.** They were four questions wearing one
constant, and each answers differently once provisioning owns the table:

| Call site | Asks now | Statement |
| --- | --- | --- |
| `ArchiveWriter` precondition | must the table be there? | `ArchiveExistsCommand`, sense inverted |
| `ArchiveWriter` refusal | did a run already fill it? | new `ArchiveIsSeededCommand` |
| `ArchiveTemplate.SeedAsync` | is the template already seeded? | new `ArchiveIsSeededCommand` |
| `ArchiveWriterTransactionTests` | did the rollback leave anything? | new `ArchiveIsSeededCommand` |
| `ArchiveDatabaseTests:44` | does a `template0` database carry an archive? | `ArchiveExistsCommand`, unchanged |

`ArchiveIsSeededCommand` is rows-or-day-partitions: `EXISTS (SELECT 1 FROM public.trends)` or a
`pg_inherits` partition whose name is not `tpdefault`, which arrives with the provisioning.

**Measured.**

- **Evidence 1** — `dotnet build SemiPlot.slnx -c Release`: exit 0, 0 warnings, 0 errors.
- **Evidence 3** — `SEMIPLOT_REQUIRE_DB=1 dotnet test .../SemiPlot.Tests.Data.csproj -c Release`:
  **381 passed, 0 skipped, 0 failed, 12 s**.
- **Evidence 4**, run here rather than left to Task 5 because the checkbox above claims it:
  `SEMIBASE_EXE` unset and `C:\Users\admin\bin\semibase.exe` moved off `PATH` (`which semibase` finds
  nothing) — **381 passed, 0 skipped, 0 failed, 11 s**. The binary was restored afterwards.
- **Evidence 5** — the wait strategy is `psql --host localhost --port 5432 --username postgres
  --dbname semiplot_provisioned --tuples-only --no-align --command "SELECT count(*) FROM
  public.trends;"`, run as an exec. Checked by hand against the built image: the first attempt is
  `connection to server at "localhost" (::1), port 5432 failed: Connection refused`, and the command
  first succeeds (`0`) at attempt 6, after the log carries
  `running /docker-entrypoint-initdb.d/10-semibase.sh`, then
  `Done: bench completed against /var/run/postgresql/.s.PGSQL.5432 (semiplot_provisioned).`, then
  `PostgreSQL init process complete; ready for start up.` The TCP host is what buys the ordering: the
  entrypoint's temporary server listens on the unix socket only.
- **Evidence 6** — the fixture built without `SEMIBASE_WRITER_PASSWORD` on the container surfaces
  `SEMIPLOT_REQUIRE_DB is set, so an unavailable runtime fails instead of skipping: no container
  runtime started a bench image over postgres:17-alpine: Container <id> exited with code 1.` The
  container log it carries ends with
  `error: role scada_writer does not exist and no password was given to create it`. The edit was
  reverted after the measurement.

⚠️ **Deviation — `COPY --chmod` needs BuildKit, which the in-process build does not use.** Task 1
built the image with `docker build`, which is BuildKit by default; the fixture builds through the
Docker Engine API, which is the classic builder, and the first run failed with `the --chmod option
requires BuildKit`. `bench/Dockerfile` now carries a plain `COPY` plus `RUN chmod 0755`, which states
the same mode on both builders.

⚠️ **Deviation — `TheResolvedBinaryReportsThePinnedVersion` retires here, not in Task 3.** It is the
one test that cannot survive the checkbox above it: it spawns `Server.SemibaseExecutable`, which is
null on the container path by design now. Keeping it would have failed Evidence 3, and skipping it
would have failed Evidence 3's "zero skips". The gated count is therefore **381, not 382**, and Task
3's remaining share of the drop is `SchemaResourceTests` (11 cases) alone, landing on 370.
`SemibaseBinary.PinnedVersion` still stands and is still Task 3's to retire.

**[decision] `semibase bench` runs on the `SEMIPLOT_TEST_PG` path too, against
`semiplot_provisioned`.** Both paths then reach the same state — one provisioned source database that
everything else clones — so `ArchiveTemplate` and the three tests needing a provisioned clone carry no
branch at all. Keeping that path on per-database provisioning would have meant two shapes of the same
fixture.

**[decision] The bench image is tagged with a digest of `BASE_IMAGE`.** `semiplot-bench:<sha256 of
SEMIPLOT_PG_IMAGE, first 12 hex>`, built with `WithDeleteIfExists(false)` and `WithCleanUp(false)`.
Changing `SEMIPLOT_PG_IMAGE` therefore cannot be served a build made over the previous base, and the
image survives the run so the next build is a cache lookup.

**[decision] `PGPASSWORD` is set on the container.** The wait strategy's `psql` runs as an exec, which
inherits only the container environment, and the base image authenticates TCP logins with a password.
Setting it is what makes the readiness query possible over TCP, and TCP is what excludes the
entrypoint's socket-only temporary server.

**[decision] `SemibaseProvisioner.CreateAsync` became `ProvisionAsync`.** After this task it has one
caller, and the name `Create` reads as the retired `semibase create`. It returns a stated failure
rather than throwing when `SemibaseExecutable` is null, so a container-path caller would surface an
unavailable reason rather than a stack trace.

**Scope guard held.** No DDL is transcribed on this side, no shape assertion was written to replace
anything, and `StartupFailureMapper` is untouched.

### Task 3: Remove what the switch orphans

**Files:**
- Delete: `sql/semiplot_dev.sql`
- Delete: `SemiPlot/SemiPlot.Tests.Data/SchemaResourceTests.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixtureTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseBinary.cs`
- Modify: `sql/README.md`

- [x] retire `sql/semiplot_dev.sql` (repository root, referenced as `..\..\sql\semiplot_dev.sql`),
      its `EmbeddedResource` entry, `ReadSchemaScript`, and `SchemaResourceTests`
- [x] **drop the schema term from `ArchiveTemplate.ComputeName` (`:118`)** — it is the third consumer
      of the resource and the one that breaks the build if missed. The module version and the slice
      options still discriminate
- [x] delete `TheResolvedBinaryReportsThePinnedVersion` and retire `SemibaseBinary.PinnedVersion`: a
      pin naming v0.1.0 cannot coexist with a path that issues `bench`
- [x] `SemibaseBinary`'s `PATH` search dies. Judge whether the type survives: the `SEMIPLOT_TEST_PG`
      path still needs to name the executable and `SEMIBASE_EXE` is how. If a one-line environment
      read replaces 69 lines, take that; if the type still earns itself, say why
- [x] `sql/README.md`'s first two paragraphs describe the retiring file; the real-archive CSV it also
      covers stays
- [x] run Evidence 1, 2 and 3

**Measured.**

- **Evidence 1** — `dotnet build SemiPlot.slnx -c Release`: exit 0, 0 warnings, 0 errors.
- **Evidence 2** — `dotnet test .../SemiPlot.Tests.csproj -c Release`: **370 passed, 0 skipped,
  0 failed, 1 s** — unchanged, and `StartupFailureMapper` is untouched.
- **Evidence 3** — `SEMIPLOT_REQUIRE_DB=1 dotnet test .../SemiPlot.Tests.Data.csproj -c Release`:
  **370 passed, 0 skipped, 0 failed, 12 s**. That is Task 2's 381 minus `SchemaResourceTests`
  (11 cases), the whole of this task's share.
- `dotnet format SemiPlot.slnx --verify-no-changes`: exit 0.

**[decision] `SemibaseBinary` survives, cut from 69 lines to 25.** What is left is the `SEMIBASE_EXE`
read, the `File.Exists` check and the two failure messages — the `PATH` search, `PinnedVersion`,
`WindowsFileName`, `UnixFileName` and the injectable `Resolve(configuredPath, searchDirectories)`
overload are gone. It stays a type rather than moving into `PostgresContainerFixture` because that
class already stands at 295 lines against the 300-line preference in `CLAUDE.md`, and the
SEMIBASE_EXE contract — variable name, resolved full path, the two ways it can fail — reads as one
thing in one file. The missing-variable message now names `SEMIPLOT_TEST_PG` as the reason the binary
is wanted, which the old "download the v0.1.0 release" text could not.

**[deviation] `TheResolvedBinaryReportsThePinnedVersion` was already gone.** Task 2 retired it, for
the reason recorded there, so `PostgresContainerFixtureTests.cs` needed no edit in this task despite
being listed under **Files**. `SemibaseBinary.PinnedVersion` was still standing and retired here.

**[deviation] `ExplainPlanTests.cs:44` was edited although it is not in the Files block.** Its comment
cited `(sql/semiplot_dev.sql)` as the authority for `tpdefault` being empty; the file no longer
exists, so the citation now names the provisioning instead. Comment only, no assertion touched.

**[decision] `sql/README.md` keeps the dump provenance and loses the schema derivation.** The
`## Where semiplot_dev.sql came from` section went with the file, but the two facts the CSV section
depends on — the dump lives outside the repository as `<path-to-dump>`, and `pg_restore` needs its
full path on Windows — moved up into a `## The customer dump` section rather than dying with it. The
directory now holds only this README; whether `sql/` should exist at all is a documentation question
`docs/architecture/README.md:36` raises, which is Task 6's.

### Task 4: Update CI

**Files:**
- Modify: `.github/workflows/ci.yml`

- [x] drop the `gh release download` step and the `SEMIBASE_EXE` it sets — the container path needs
      neither
- [x] confirm `data-tests` still sets `SEMIPLOT_REQUIRE_DB` so an unavailable runtime is a failure
      rather than a silent skip
- [x] confirm the job pulls `ghcr.io/semiteq/semibase:latest` anonymously; the package is public
- [x] the guard here is the workflow file and the CI run, not `dotnet test`

**What changed.** The `install semibase` step and its two-line comment are gone; nothing else in the
job moved. `data-tests` is now `checkout → setup .NET → restore → build → test`, and its only
`env` entry is still `SEMIPLOT_REQUIRE_DB: "1"`, which is what turns an unavailable container runtime
into a failure instead of a skip. A comment on the `test` step states where the provisioner now comes
from, replacing the version-pin comment that went with the step.

**Measured**, against this task's own commit rather than against HEAD. Task 6 edited the same job
again and removed the two `sql/**` path filters (`83c4656`), so the diff shape below is a record of
what this task did, not a description of the workflow as it now stands.

- `yaml.safe_load` parses the file; all three jobs are present, and every one of them is
  `checkout → setup .NET → restore → build → test`.
- `git diff -U0` yields exactly two hunks, both at line 110 and beyond, inside `data-tests`
  (which starts at line 87). `build-and-test` and `ui-tests-linux` are byte-identical.
- No `run` body was edited; the three remaining `data-tests` bodies concatenated pass `bash -n`.
- `grep -n "SEMIBASE\|GH_TOKEN\|gh release"` on the workflow returns nothing outside the new
  comment. `permissions: contents: read` stays — `actions/checkout` still needs it; only the step
  that consumed `github.token` is gone.
- The file keeps no BOM and LF endings, as it had.

**The package is public — checked, not assumed.** After `docker logout ghcr.io` (leaving
`~/.docker/config.json` with an empty `auths`), a credential-free `GET
https://ghcr.io/token?scope=repository:semiteq/semibase:pull&service=ghcr.io` returns a token, and
that token fetches the manifest: `HTTP 200`, digest
`sha256:533adc17a4f934827c18e5ad65cebae220daf70db2ba6b837781204496f6291f`. `docker manifest inspect
ghcr.io/semiteq/semibase:latest` succeeds the same way. So the runner needs no `docker login` and no
token to pull the provisioner layer.

**Only the next CI run can confirm** that `ubuntu-latest`'s Docker daemon builds the bench image and
starts the container inside the job — the daemon's presence and the image build were never exercised
on a runner, only on this developer host. If the daemon were missing, `SEMIPLOT_REQUIRE_DB: "1"`
turns that into a red job rather than a green one full of skips, which is the point of keeping it.

**[decision] The comment moved to the `test` step rather than being deleted outright.** The step it
described is gone, but the question it answered — where does the provisioner come from, and does the
runner need credentials for it — is still the first thing a reader of this job asks. The comment now
answers it for the image path.

**[decision] The `sql/**` path filter stays, though `sql/` now holds only `README.md` and `!**.md`
excludes it.** That trigger entry is dead as the tree stands. Removing it would preempt Task 6's open
question of whether `sql/` should exist at all (`docs/architecture/README.md:36`); a stale filter
entry costs a workflow nothing, and whoever settles the directory settles the filter with it.

> **Superseded by Task 6.** That open question was settled the way this decision anticipated: `sql/`
> does not survive, and both `sql/**` filters were removed with it in `83c4656`. See
> *[decision] `sql/` does not survive* under Task 6. The reasoning above records why the entry
> outlived Task 4; it does not describe the workflow at HEAD, which carries no `sql/**` entry.

**[deviation] None.** Every checkbox landed as written.

### Task 5: Verify acceptance criteria

- [x] run every Evidence item and record what each reported
- [x] **Evidence 4 is the one that proves the slice**: unset `SEMIBASE_EXE`, ensure no `semibase` is
      on `PATH`, and confirm the gated suite still passes
- [x] confirm the count drop equals `SchemaResourceTests` plus
      `TheResolvedBinaryReportsThePinnedVersion`, and that the two read tests still exist
- [x] confirm `SemiPlot.Tests` is still 370 passed, 0 skipped, 0 failed
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0
- [x] confirm every tracked `.cs` file still begins `ef bb bf` and no tracked `.md` gained one
- [x] confirm the scope guard held: no second transcription of the vendor DDL on this side, no shape
      assertion written to replace the retiring one, no change to what the provisioning does, and
      `StartupFailureMapper` untouched

**Measured against the branch as it stands at `0c4fd33`, every item re-run rather than quoted from an
earlier task.**

| Evidence | Reported |
| --- | --- |
| 1 - build | `dotnet build SemiPlot.slnx -c Release`: exit 0, **0 warnings, 0 errors**, 3.47 s |
| 2 - UI suite | **370 passed, 0 skipped, 0 failed, 1 s** |
| 3 - gated suite | **370 passed, 0 skipped, 0 failed, 12 s** |
| 4 - no machine binary | **370 passed, 0 skipped, 0 failed, 11 s** |
| 5 - wait strategy | in place and observed; ready only after `bench` logged completion |
| 6 - broken provisioning | container exits **1**; the fixture surfaces the container log |
| 7 - `SEMIPLOT_TEST_PG` path | **ran**: 370 passed, 0 skipped, 0 failed, 12 s |
| 8 - format and encoding | `--verify-no-changes` exit 0; BOMs as required |

**Evidence 4, run rather than reasoned about.** `SEMIBASE_EXE` was empty in the shell, and the only
`semibase` on this machine - `C:\Users\admin\bin\semibase.exe`, v0.1.0 - was renamed to
`semibase.exe.task5-aside` for the run. `which -a semibase` and `which -a semibase.exe` both reported
nothing across the whole `PATH`. `SEMIPLOT_REQUIRE_DB=1 dotnet test .../SemiPlot.Tests.Data.csproj -c
Release` then reported **370 passed, 0 skipped, 0 failed, 11 s**, identical to Evidence 3. The binary
was renamed back afterwards and `semibase --version` again reports `v0.1.0`.

**The count drop is exactly the two retirements.** Task 1's baseline was 382; the gated suite now
reports 370. The 12 cases are `SchemaResourceTests` - 2 `[Fact]` plus 1 `[Theory]` carrying 9
`[InlineData]`, counted from `git show master:.../SchemaResourceTests.cs`, so 11 - and
`TheResolvedBinaryReportsThePinnedVersion`, 1. Both read tests still exist and still assert the same
thing: `git diff master...HEAD` on `PostgresExtentReadTests.cs` and `PostgresHistoryReadTests.cs`
touches only how `AProvisionedButUnseededDatabaseFailsNamingTrends` reaches its state
(`CreateEmptyDatabaseAsync` plus the provisioner becomes `CloneProvisionedAsync` plus
`DropTrendsCommand`), and no `Assert` line moved. `PinnedVersion` and
`TheResolvedBinaryReportsThePinnedVersion` return nothing anywhere under `SemiPlot/`.

**Evidence 5, observed against a hand-started container rather than assumed.** The strategy is
`Wait.ForUnixContainer().UntilCommandIsCompleted(...)` running `psql --host localhost --port 5432
--username postgres --dbname semiplot_provisioned --tuples-only --no-align --command "SELECT count(*)
FROM public.trends;"` (`PostgresContainerFixture.cs:167,195-214`). Polling that exact command against
a fresh `semiplot-bench:fc0cf5409512`: attempts 1 and 2 fail with `connection to server at
"localhost" (::1), port 5432 failed: Connection refused`; attempt 3 returns `0`. The container log
reaches `running /docker-entrypoint-initdb.d/10-semibase.sh`, then
`[ OK ] public.trends created as scada_writer, partitioned by t, with the tpdefault partition`,
`[ OK ] semiplot_reader reads public.trends`,
`Done: bench completed against /var/run/postgresql/.s.PGSQL.5432 (semiplot_provisioned).`,
`PostgreSQL init process complete; ready for start up.`, and only then
`listening on IPv4 address "0.0.0.0", port 5432`. The TCP host in the strategy is what buys the
ordering - before that line the server answers on the unix socket only.

**Evidence 6, at both levels.** The bench image run with `SEMIBASE_READER_PASSWORD` set and
`SEMIBASE_WRITER_PASSWORD` omitted logs
`error: role scada_writer does not exist and no password was given to create it` and the container
reaches `exited 1`; no port ever serves. With the fixture's
`.WithEnvironment(SemibaseProvisioner.WriterPasswordVariable, ContainerWriterPassword)` line
temporarily removed, the suite surfaces

```
System.InvalidOperationException : SEMIPLOT_REQUIRE_DB is set, so an unavailable runtime fails
instead of skipping: no container runtime started a bench image over postgres:17-alpine: Container
83c63ee1d9e0fbc4c18a7e1203253d4f30d2185f69e9e5bb1615f77a46b08a32 exited with code 1.
2026-08-24T11:38:38.052481007Z error: role scada_writer does not exist and no password was given to
create it
```

The fixture edit was reverted; `git status` is clean.

**Evidence 7 ran - the expectation that it could not is now out of date.** SemiBase publishes
`semibase_0.3.0_windows_amd64.exe` as a v0.3.0 release asset, so the machine's v0.1.0 was not the
only option. The asset was downloaded to the scratchpad (never onto `PATH`, so Evidence 4 stays
honest) and reports `v0.3.0`, with `site` and `bench` as its two commands. A plain
`postgres:17-alpine` container was started by hand on port 55433 with no provisioning of any kind -
a server the fixture did not create - and the suite was run with
`SEMIPLOT_TEST_PG="Host=localhost;Port=55433;Database=postgres;Username=postgres;Password=..."`,
`SEMIBASE_EXE` naming the downloaded binary, and the two role passwords in the environment:
**370 passed, 0 skipped, 0 failed, 12 s**. That the binary really was spawned is visible on the
server afterwards - `semiplot_provisioned` exists, `scada_writer` and `semiplot_reader` exist, and
`select count(*) from public.trends` in that database returns `0`. The stock image's `pg_hba.conf`
admits `semiplot_reader`, so `bench`'s new reader TCP login passed. The container was removed after
the run.

**Evidence 8.** `dotnet format SemiPlot.slnx --verify-no-changes`: exit 0. All **200** tracked `.cs`
files begin `ef bb bf`. Of the 38 tracked `.md` files exactly one carries a BOM,
`docs/plans/completed/20260819-postgres-wire-up.md` - and `git show master:` on it reports
`ef bb bf` too, so it is inherited, not gained. The only two `.md` files this branch touches,
`docs/plans/20260824-semibase-container-provisioning.md` and `sql/README.md`, carry no BOM.

**Scope guard held, checked from `git diff master...HEAD`.**

- **No second transcription of the vendor DDL.** `git ls-files '*.sql'` returns nothing at all, and
  the added lines carry no `CREATE TABLE`, `PARTITION BY`, `CREATE ROLE`, `GRANT`,
  `ALTER DEFAULT PRIVILEGES` or `CREATE INDEX`. The only DDL-adjacent addition is
  `AND partition.relname <> 'tpdefault'` inside `ArchiveIsSeededCommand` - a predicate over
  `pg_inherits`, which is a question about state, not a statement of shape.
- **No shape assertion replaced the retiring one.** The diff adds no test file; `bench/Dockerfile`
  and `bench/provision.sh` are the only new files, and no existing test gained an assertion over the
  archive's columns, constraints or partitioning.
- **Nothing changed about what the provisioning does.** `provision.sh` issues
  `/semibase bench --host /var/run/postgresql --database semiplot_provisioned` and nothing else - no
  extra flag, no SQL of its own, no `--expected-major`.
- **`StartupFailureMapper` untouched.** It does not appear in `git diff master...HEAD --name-only`.
  Its stale `semibase create` advice stays for `missing-relation-probe-removal`, as the plan states.

**[decision] Evidence 7 was made runnable rather than reported as unavailable.** The standing note
said the machine's `semibase` is v0.1.0 and to say plainly if the item could not run. It could:
v0.3.0 ships a Windows binary as a release asset, and "a server the fixture did not create" is
satisfied by a stock `postgres:17-alpine` container started by hand. Downloading to the scratchpad
rather than to `PATH` keeps Evidence 4's premise intact - and Evidence 4 had already run and passed
before the download.

**[deviation] None of the expected counts moved.** `SemiPlot.Tests` 370/0/0 and
`SemiPlot.Tests.Data` 370/0/0 under `SEMIPLOT_REQUIRE_DB=1` both match the expectation exactly, on
the container path and on the `SEMIPLOT_TEST_PG` path alike.

### Task 6: [Final] Update documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/architecture/bench.md`
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `docs/architecture/postgres-instance.md`
- Modify: `docs/architecture/postgres-topology.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/README.md`
- Modify: `readme.md`

- [x] `CLAUDE.md`: the *Gated data tests* table describes `SEMIBASE_EXE` as searching `PATH`; `:19`
      states the seeder "refuses a database that already holds `public.trends`" — the sentence this
      slice inverts; `:216` states the embedded resource
- [x] `bench.md`: the provisioning step moved into the image and the archive table is no longer the
      seeder's
- [x] `testing-strategy.md`: the ownership table assigns the archive DDL to this repository, and the
      pinning section names the machine-resolved binary as the one gap in the rule — that gap closes
- [x] `postgres-instance.md:12,45,61,77,97` and `postgres-topology.md:17,18,139,141,173` describe
      `semibase config / create / verify` and pin `v0.1.0` in a diagram
- [x] `data-integration.md:512` names `semibase create` as a remedy
- [x] `docs/architecture/README.md:36` lists `semiplot_dev.sql` as a `sql/` data file
- [x] `readme.md:69-74` (Russian) tells the reader to install a `semibase` binary — keep it Russian
- [x] leave `StartupFailureMapper`'s stale advice alone and say why in the plan: it belongs to
      `missing-relation-probe-removal`
- [x] deferred to the delivery step — exec never moves the plan. Archiving to `docs/plans/completed/`
      runs after the operator has tested the branch.

**What each document now says.**

| Document | Corrected to |
| --- | --- |
| `CLAUDE.md` | the seeder requires `public.trends` and refuses rows or day partitions; the gated suite needs a container runtime and nothing else; `SEMIBASE_EXE` is read on the `SEMIPLOT_TEST_PG` path alone and nothing searches `PATH`; `SEMIPLOT_PG_IMAGE` is `--build-arg BASE_IMAGE`; the embedded-resource sentence is gone |
| `bench.md` | the ownership row moves `public.trends` with `tpdefault` to `semibase bench`; a new *Where the provisioning comes from* section states the image, the init hook, the socket, `set -e`, the in-process build, the TCP wait strategy and the empty-`PGDATA` rule; the template digest loses its schema term; the application-bench recipe runs the bench image instead of a stock image plus a machine binary |
| `testing-strategy.md` | the ownership table names `public.trends` as SemiBase's; the pinning section's "one dependency does not yet meet the rule" paragraph is replaced — every dependency meets it now, and the `latest` tag is stated as a deliberate choice with its cost |
| `postgres-instance.md` | `semibase site` / `semibase bench` replace `config / create / verify`; the `tpdefault` paragraph stops citing the removed `verify` and states that the partition arrives empty from the provisioning |
| `postgres-topology.md` | the ownership subgraph names the two surviving commands; the state-machine transitions read `provisioning`; the bench diagram gains `semiplot_provisioned` as the cloned source and drops the `v0.1.0` pin |
| `data-integration.md` | the three `semibase create` remedies become `semibase site`, the command a site operator has |
| `docs/architecture/README.md` | the `sql/` data-file paragraph becomes the fixture CSV, and states that no archive schema is carried here |
| `readme.md` (Russian) | the requirements row and the test section drop the `semibase` binary and the `SEMIBASE_EXE` block; the provisioner arrives with the image |

**[decision] `sql/` does not survive; its README moves to
`SemiPlot/SemiPlot.Tests.Data/Fixtures/README.md`.** Task 3 left the question open with the directory
holding one file. Exactly two things referenced it — `docs/architecture/sources.md:64` and the
comment at `RealArchiveFixture.cs:9` — and everything it documents is the provenance of
`Fixtures/real-archive-rows.csv`, which lives in that directory with the fixture code that reads it.
A directory named `sql/` holding no SQL is worse than a stale line: it is an invitation to put a
schema back where this slice removed one. Both references and the two `sql/**` CI path filters moved
with it, which settles the entry Task 4 deliberately left standing.

**[decision] The site-side "who creates `trends`" model stays as it is, in every document.**
`postgres-instance.md`'s four states, `postgres-topology.md`'s state machine and
`data-integration.md`'s error table all rest on a missing `trends` meaning the SCADA has not run.
v0.3.0 makes that false, and `StartupFailureMapper.cs:120-133`, `MissingRelationProbe.cs:69-70`,
`ArchiveNotInitialisedError.cs:13,21` and `StartupFailureMapperTests.cs:103,114,124` implement it.
That whole model is one piece and belongs to `missing-relation-probe-removal`, which owns the
mapper's cold path. Correcting the documents here and not the code would leave the architecture
describing a client that does not exist — a worse state than one named deferral. Only the command
*name* was corrected where a document states a remedy, because `semibase create` exists on no path.
`StartupFailureMapper` itself is untouched, as Evidence 2 requires.

**[deviation] Three `.cs` comments outside the **Files** block were corrected.** The validation gate
is "no `git grep` hit for a command SemiBase no longer has", and three hits were bench-side rather
than mapper-side: `SeededArchiveTests.cs:10` and `StatementTimeoutReadTests.cs:95` attributed the
reader's 30 s `statement_timeout` to `semibase create`, and `PostgresCatalogReadTests.cs:29` named
"SemiBase v0.1.0's semiplot_tags" although the bench now runs `latest`. Comments only; no assertion,
signature or literal moved. The remaining hits are exactly the mapper's cold path and its three
dependents, left standing by the decision above.

**Measured.**

- `dotnet build SemiPlot.slnx -c Release`: exit 0, **0 warnings, 0 errors**, 8.77 s.
- `dotnet format SemiPlot.slnx --verify-no-changes`: exit 0.
- BOMs: none of the ten changed `.md` files carries one, and the only tracked `.md` with a BOM is
  still the inherited `docs/plans/completed/20260819-postgres-wire-up.md`. Every edited `.cs` file
  still begins `ef bb bf`.
- `git grep -E "semibase (create|config|verify|all)"` outside `docs/plans/` returns only
  `StartupFailureMapper.cs`, `StartupFailureMapperTests.cs`, `MissingRelationProbe.cs` and
  `ArchiveNotInitialisedError.cs`. `git grep "v0.1.0"` and `git grep "semiplot_dev.sql"` outside
  `docs/plans/` return nothing.
- Every prose line added to a `.md` is at most 100 characters.

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Nothing requires manual verification.** Every acceptance item is a command. The gated suite needs a
container runtime; with `SEMIPLOT_REQUIRE_DB=1` its absence is a failure rather than a silent pass.

**What this slice bets on.** That SemiBase's `latest` stays compatible with this reader. Delivered
installations update neither service, so the only pair ever newly deployed is the newest provisioner
with the current reader — which is exactly the pair this bench now exercises on every run. The cost
is that one unchanged commit can pass today and fail tomorrow; the mitigation is that the resolved
version is printed into the test output.

**Left for the next slice, deliberately.** `StartupFailureMapper` tells the operator to run
`semibase create`, which v0.3.0 removed, and reasons that SemiBase never creates `trends`, which
v0.3.0 makes false. `missing-relation-probe-removal` owns that mapper's cold path and corrects both.

**Remaining slices**

- `missing-relation-probe-removal` — the `42P01` probe goes; its static fallbacks already answer.
- `postgres-live-edge-and-demo` — the realtime poll, the fresh tail, the `--follow` writer and the
  stub's retirement.

## Execution record

**Executed by exec:**

- branch: semibase-container-provisioning

### Tasks and commits

| Task | Commit |
| --- | --- |
| 1. Add the image and prove it in isolation | `9cc6c70` build(tests): add the self-provisioning bench image |
| 2. Switch the fixture to the image and move every consumer with it | `2aae303` test: provision the bench from the container image |
| 3. Remove what the switch orphans | `5160ecd` refactor(bench): retire the embedded archive schema |
| 4. Update CI | `0c4fd33` ci: drop the semibase binary install from data-tests |
| 5. Verify acceptance criteria | `ab11f50` test: verify the container-provisioned bench |
| 6. Update documentation | `83c4656` docs: record image-carried bench provisioning |

Seven further commits came from review: `f7b7001`, `a938b6a`, `15cef9e` from the first fixer round,
and `5cc993a`, `268a5fc`, `1300899`, `c302bbb` from the second.

### Review phases

**Comprehensive review, two agents in parallel.** Both reported the pull mechanism as the central
defect: the repository tracks `ghcr.io/semiteq/semibase:latest`, and nothing on the build path moved
that tag, so a machine with the image cached built a frozen provisioner. The two disagreed on
whether Testcontainers sets `Pull` on the build request; the disagreement was settled by measurement
rather than by reading, with a local registry serving a marked v1/v2 image against the classic
builder.

**Smells pass plus comment audit.** Produced the change-narration sweep and the set of small
corrections; no correctness finding of its own.

**Fixer rounds, two.** The first delivered the pull as its own step and the minor corrections around
it. The second worked the triaged groups A to E from both comprehensive reviewers.

**Critical-only review, two agents in parallel.** Both returned NO CRITICAL FINDINGS. One ran from a
fresh clone of the branch with the Linux CI leg simulated in `mcr.microsoft.com/dotnet/sdk:10.0`.

**The external `codex` phase did not run: `codex` is not installed on this machine.**

### Findings fixed

**The pull mechanism was half-delivered.** Task 2 built the bench image without ever moving the
`latest` tag it builds `FROM`. Setting `Pull` on the build request was measured and rejected — with
the registry unreachable and a usable local base image present, `docker build --pull` fails hard
rather than falling back — so the pull became its own step in an internal `ProvisionerImage`, with
an unreachable registry tolerated. Four parts landed with it: a structured resolve result
(`Result<ProvisionerResolution>` carrying digest, version and staleness reason, joined only in
`Describe()`); a digest-bound `PROVISIONER_IMAGE` build argument set to `RepoDigests[0]`, so the
image built is the image pulled; a guard against a null
`TestcontainersSettings.OS.DockerEndpointAuthConfig`, which on a machine with no runtime or a
stopped daemon turned the skip reason into a `NullReferenceException`; and the stale-provisioner
warning written to standard error, measured to survive `--logger "console;verbosity=normal"` where
`TestOutputHelper.WriteLine` from a passing test is dropped.

**The documents contradicted themselves about who creates `public.trends`.** Resolved by stating in
`postgres-instance.md` that SemiBase creates the table in both `semibase site` and `semibase bench`,
and by naming the code's lag: `StartupFailureMapper`, `MissingRelationProbe` and
`ArchiveNotInitialisedError` carry the older model until `missing-relation-probe-removal`.
`postgres-topology.md` and `data-integration.md` relabel their transitions and point at that
statement; `history-read-path-evaluation.md` took the plain factual fix.

**A change-narration sweep over comments and architecture documents.** Every added line was grepped
for now / no longer / previously / stopped / began / once / still and rewritten as a statement of
what is true. `docs/plans/*` was excluded — a plan record narrates its own change by design.

**A set of small corrections.** `provision.sh` takes the database name from the C# constant through
`SEMIPLOT_PROVISIONED_DATABASE` with `: "${VAR:?}"`, so an unset name aborts the entrypoint;
`ProvisionAsync` dropped its now-redundant `database` parameter; the `DatabaseGate` comment stopped
equating a missing container runtime with a missing `semibase` binary; two tests were renamed to
match their sibling; `Docker.DotNet.Enhanced` 4.3.3 was pinned in `Directory.Packages.props`; the
application-bench recipe in `bench.md` seeds a clone instead of the pristine source; Task 4's record
was marked superseded where Task 6 removed the two `sql/**` filters; and
`ArchiveDatabase.EmptyAsync` with `CreateEmptyDatabaseAsync` and their two guard-only tests were
deleted.

### Findings declined

- **`PostgresContainerFixture` runs past the 300-line preference** (355). The file is one container
  lifecycle across both provisioning paths; splitting it by line count would spread that lifecycle
  over types with no separate purpose each.
- **The `StartupFailureMapper` deferred set.** The old missing-`trends` model is one piece, owned by
  `missing-relation-probe-removal`. Evidence 2 requires the mapper untouched here; the lag is named
  in the documents instead.
- **Merging `ArchiveDatabase.CopyAsync` into `CloneAsync`.** They differ in both directions:
  `CloneAsync` invents a name and returns a disposable handle, `CopyAsync` takes a stated name and
  returns nothing to dispose. One merged method would carry a nullable name and a nullable return.
- **`BenchImageFor`'s digest tag scheme.** The tag digests the base image name so two
  `SEMIPLOT_PG_IMAGE` values cannot share one built image. It is a cache key, not an identity — the
  build runs every run and the provisioner identity is already pinned by the digest build argument.
- **The `SEMIPLOT_TEST_PG` clone race** (`55006` immediately after the spawned `semibase bench`
  exits). Any honest fix is a bounded retry or a wait on `pg_stat_activity` beside
  `ArchiveDatabase.CloneAsync`, not a line at the call site, and the race does not exist on the
  container path — the only path CI runs.

### Final measurements

- `dotnet build SemiPlot.slnx -c Release`: **0 warnings, 0 errors**.
- `dotnet format SemiPlot.slnx --verify-no-changes`: exit 0.
- `SemiPlot.Tests`: **370 passed**.
- `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1`: **369 passed, 0 skipped**.

The data count moved 382 → 370 → 371 → 369 across the run: −11 from the retired
`SchemaResourceTests` and −1 for the pinned-version test, then +1 for the provisioner identity
report, then −2 for the empty-database tests removed with `CreateEmptyDatabaseAsync`.
