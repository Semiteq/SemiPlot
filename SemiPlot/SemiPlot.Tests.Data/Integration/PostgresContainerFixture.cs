using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

using FluentResults;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// One server per test run. A container start costs seconds while a CREATE DATABASE costs under a
// second, so the container is the wrong isolation unit and the database is the right one.
//
// The container path provisions itself: the image built from bench/ carries the provisioner and runs
// it from the entrypoint's init hook, so nothing is resolved from the machine running the suite. Only
// the SEMIPLOT_TEST_PG path needs a semibase binary, and it resolves one in its own branch.
//
// Initialisation never throws. The runtimes it needs are optional on a developer machine, so their
// absence is captured as a reason and handed to DatabaseGate, which skips or fails according to
// SEMIPLOT_REQUIRE_DB.
public sealed class PostgresContainerFixture : IAsyncLifetime
{
	public const string SuperuserName = "postgres";

	// The container is ephemeral and holds no secret, and a developer must not need environment
	// variables to run the suite, so the container path carries fixed dummy passwords. Only the
	// SEMIPLOT_TEST_PG path reads real ones: the superuser's from the connection string, the two role
	// passwords from SEMIBASE_WRITER_PASSWORD and SEMIBASE_READER_PASSWORD.
	public const string ContainerSuperuserPassword = "semibase-container-superuser";

	public const string ContainerWriterPassword = "semibase-container-writer";

	public const string ContainerReaderPassword = "semibase-container-reader";

	public const string MaintenanceDatabase = "postgres";

	private const ushort PostgresPort = 5432;

	// The build context is copied to the output directory: a test assembly runs from there with no path
	// back to the repository.
	private const string BenchContextDirectory = "bench";

	private const string BaseImageArgument = "BASE_IMAGE";

	private const string ProvisionerImageArgument = "PROVISIONER_IMAGE";

	// The provisioned database name travels into the container rather than being transcribed in
	// provision.sh, so SemibaseProvisioner.ProvisionedDatabase stays the one place it is written.
	private const string ProvisionedDatabaseVariable = "SEMIPLOT_PROVISIONED_DATABASE";

	private const string BenchImageRepository = "semiplot-bench";

	// The wait strategy's psql runs as an exec, which inherits the container environment and nothing
	// else, and a TCP login to the base image is password-authenticated.
	private const string PasswordVariable = "PGPASSWORD";

	// CREATE DATABASE ... TEMPLATE fails while another session holds the source, so a clone must never
	// overlap another clone or a drop. Never disposed: disposing it here would race the clone disposals
	// xunit runs while tearing the collection down.
	private readonly SemaphoreSlim _creationGate = new(1, 1);

	private PostgreSqlContainer? _postgreSqlContainer;

	private PostgresServer? _postgresServer;

	private string? _templateDatabase;

	public string? UnavailableReason { get; private set; }

	public bool IsAvailable => UnavailableReason is null;

	// The provisioner this run's databases came from, so a `latest` that fails tomorrow on an unchanged
	// commit can be told apart from a change in this repository. Null on the SEMIPLOT_TEST_PG path,
	// where the operator named the binary and there is nothing left to resolve.
	internal ProvisionerResolution? Provisioner { get; private set; }

	public PostgresServer Server =>
		_postgresServer ?? throw new InvalidOperationException(
			UnavailableReason ?? "The fixture was used before it was initialised.");

	public string TemplateDatabase =>
		_templateDatabase ?? throw new InvalidOperationException(
			UnavailableReason ?? "The fixture was used before it was initialised.");

	public Task<ArchiveDatabase> CloneTemplateAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.CloneAsync(Server, _creationGate, TemplateDatabase, cancellationToken);
	}

	// A database carrying the roles, the grants and an empty public.trends, and no seeded row. The clone
	// is how a test reaches that state: provisioning it a second time would spawn a binary the container
	// path deliberately does not have.
	public Task<ArchiveDatabase> CloneProvisionedAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.CloneAsync(
			Server,
			_creationGate,
			SemibaseProvisioner.ProvisionedDatabase,
			cancellationToken);
	}

	public void RequireAvailable()
	{
		DatabaseGate.Require(UnavailableReason, TestEnvironment.DatabaseRequired);
	}

	public async ValueTask InitializeAsync()
	{
		var server = TestEnvironment.TestServerConnectionString is { } connectionString
			? await UseExistingServerAsync(connectionString)
			: await StartContainerAsync();

		if (server.IsFailed)
		{
			UnavailableReason = server.Errors[0].Message;

			return;
		}

		var template = await ArchiveTemplate.BuildAsync(server.Value, _creationGate);

		if (template.IsFailed)
		{
			UnavailableReason = template.Errors[0].Message;

			return;
		}

		_postgresServer = server.Value;
		_templateDatabase = template.Value;
	}

	public async ValueTask DisposeAsync()
	{
		if (_postgreSqlContainer is not null)
		{
			await _postgreSqlContainer.DisposeAsync();
		}
	}

	// Testcontainers reports a missing or unreachable runtime through several exception types, and the
	// distinction does not matter here: any failure to build or to start is the same unavailable reason.
	// A provisioning that fails lands here too — the init script's `set -e` exits the container, so the
	// wait strategy never sees a ready server.
	private async Task<Result<PostgresServer>> StartContainerAsync()
	{
		try
		{
			// Ahead of the build, and the build takes the digest it resolved rather than the tag:
			// `docs/architecture/bench.md` states why.
			var provisioner = await ProvisionerImage.ResolveAsync();

			if (provisioner.IsFailed)
			{
				return Result.Fail<PostgresServer>(
					$"no container runtime started a bench image over {TestEnvironment.Image}: "
						+ provisioner.Errors[0].Message);
			}

			WarnIfStale(provisioner.Value);

			var image = new ImageFromDockerfileBuilder()
				.WithName(BenchImageFor(TestEnvironment.Image))
				.WithDockerfileDirectory(Path.Combine(AppContext.BaseDirectory, BenchContextDirectory))
				.WithBuildArgument(BaseImageArgument, TestEnvironment.Image)
				.WithBuildArgument(ProvisionerImageArgument, provisioner.Value.Digest)
				.WithDeleteIfExists(false)
				.WithCleanUp(false)
				.Build();

			await image.CreateAsync();

			var container = new PostgreSqlBuilder(image)
				.WithUsername(SuperuserName)
				.WithPassword(ContainerSuperuserPassword)
				.WithDatabase(MaintenanceDatabase)
				.WithEnvironment(SemibaseProvisioner.WriterPasswordVariable, ContainerWriterPassword)
				.WithEnvironment(SemibaseProvisioner.ReaderPasswordVariable, ContainerReaderPassword)
				.WithEnvironment(PasswordVariable, ContainerSuperuserPassword)
				.WithEnvironment(ProvisionedDatabaseVariable, SemibaseProvisioner.ProvisionedDatabase)
				.WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted(ProvisionedWaitCommand()))
				.Build();

			await container.StartAsync();

			_postgreSqlContainer = container;
			Provisioner = await DescribeProvisionerAsync(container, provisioner.Value);

			return Result.Ok(
				new PostgresServer(
					null,
					container.Hostname,
					container.GetMappedPublicPort(PostgresPort),
					SuperuserName,
					ContainerSuperuserPassword,
					MaintenanceDatabase,
					ContainerWriterPassword,
					ContainerReaderPassword));
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Result.Fail<PostgresServer>(
				$"no container runtime started a bench image over {TestEnvironment.Image}: {exception.Message}");
		}
	}

	// Asked of the container that actually provisioned this run's databases, rather than of the registry:
	// the binary it carries is the one that ran. The digest identifies the same image and is already in
	// hand, so an executable that declines to report a version costs the run nothing.
	private static async Task<ProvisionerResolution> DescribeProvisionerAsync(
		IContainer container,
		ProvisionerResolution resolution)
	{
		try
		{
			var reported = await container.ExecAsync(
				[ProvisionerImage.ExecutablePath, ProvisionerImage.VersionArgument]);

			if (reported.ExitCode == 0 && reported.Stdout.Trim() is { Length: > 0 } version)
			{
				return resolution with { Version = version };
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// The digest already names the image; the version string is only its legible form.
		}

		return resolution;
	}

	// Standard error rather than the test output: a stale provisioner is not a failure, and a passing
	// test's output is what a console logger drops. The process's own stderr reaches the CI log
	// whatever the outcome of every test is.
	private static void WarnIfStale(ProvisionerResolution resolution)
	{
		if (resolution.StalenessReason is null)
		{
			return;
		}

		Console.Error.WriteLine($"[bench] {resolution.Describe()}");
	}

	// The readiness gate is the provisioned table, not the server: a container whose init script failed
	// never becomes ready, and one still inside initdb answers on the unix socket only, so the query
	// goes over TCP to exclude the entrypoint's temporary server.
	private static string[] ProvisionedWaitCommand()
	{
		return
		[
			"psql",
			"--host",
			"localhost",
			"--port",
			PostgresPort.ToString(CultureInfo.InvariantCulture),
			"--username",
			SuperuserName,
			"--dbname",
			SemibaseProvisioner.ProvisionedDatabase,
			"--tuples-only",
			"--no-align",
			"--command",
			"SELECT count(*) FROM public.trends;"
		];
	}

	// The tag carries a digest of the base image, so a run under a changed SEMIPLOT_PG_IMAGE is never
	// served the build made over the previous base. Layers stay cached either way, which is what keeps
	// the rebuild under two seconds.
	private static string BenchImageFor(string baseImage)
	{
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(baseImage));

		return $"{BenchImageRepository}:{Convert.ToHexStringLower(digest)[..12]}";
	}

	// The named server must itself be semibase-provisioned; this path only confirms it answers, since a
	// server that refuses a connection is as unavailable as a missing container runtime. The fixture
	// still runs `semibase bench` against it, which is idempotent, so it needs a resolved binary, the
	// superuser password in the connection string and the two role passwords in the environment — a real
	// server carries real roles, and inventing passwords for them would change them.
	//
	// The binary is resolved here rather than in InitializeAsync: this is the only path that spawns one,
	// and resolving it earlier would make a machine without semibase skip the container path too.
	private static async Task<Result<PostgresServer>> UseExistingServerAsync(string connectionString)
	{
		var semibase = SemibaseBinary.Resolve();

		if (semibase.IsFailed)
		{
			return Result.Fail<PostgresServer>(semibase.Errors[0].Message);
		}

		if (TestEnvironment.WriterPassword is not { } writerPassword
			|| TestEnvironment.ReaderPassword is not { } readerPassword)
		{
			return Result.Fail<PostgresServer>(
				$"{TestEnvironment.TestServerVariable} names a real server, so "
					+ $"{SemibaseProvisioner.WriterPasswordVariable} and "
					+ $"{SemibaseProvisioner.ReaderPasswordVariable} must carry its role passwords.");
		}

		NpgsqlConnectionStringBuilder builder;

		try
		{
			builder = new NpgsqlConnectionStringBuilder(connectionString);
		}
		catch (Exception exception) when (exception is ArgumentException or FormatException)
		{
			return Result.Fail<PostgresServer>(
				$"{TestEnvironment.TestServerVariable} is not a connection string: {exception.Message}");
		}

		try
		{
			await using var connection = new NpgsqlConnection(connectionString);

			await connection.OpenAsync();
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Result.Fail<PostgresServer>(
				$"{TestEnvironment.TestServerVariable} names a server that refused a connection: {exception.Message}");
		}

		var server = new PostgresServer(
			semibase.Value,
			builder.Host ?? "localhost",
			builder.Port,
			builder.Username ?? SuperuserName,
			builder.Password ?? string.Empty,
			builder.Database ?? MaintenanceDatabase,
			writerPassword,
			readerPassword);

		var provisioned = await SemibaseProvisioner.ProvisionAsync(server);

		return provisioned.IsFailed
			? Result.Fail<PostgresServer>(
				$"semibase {SemibaseProvisioner.BenchCommand} failed against "
					+ $"{TestEnvironment.TestServerVariable}: "
					+ string.Join("; ", provisioned.Errors.Select(error => error.Message)))
			: Result.Ok(server);
	}
}
