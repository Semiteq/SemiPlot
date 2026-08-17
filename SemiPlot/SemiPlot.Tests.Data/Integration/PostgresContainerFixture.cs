using FluentResults;

using Npgsql;

using Testcontainers.PostgreSql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// One server per test run. A container start costs seconds while a CREATE DATABASE costs under a
// second, so the container is the wrong isolation unit and the database is the right one.
//
// Initialisation never throws. Both runtimes it needs — a container runtime and the semibase binary —
// are optional on a developer machine, so their absence is captured as a reason and handed to
// DatabaseGate, which skips or fails according to SEMIPLOT_REQUIRE_DB.
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

	// CREATE DATABASE ... TEMPLATE fails while another session holds the source, so a clone must never
	// overlap another clone or a drop. Never disposed: disposing it here would race the clone disposals
	// xunit runs while tearing the collection down.
	private readonly SemaphoreSlim _creationGate = new(1, 1);

	private PostgreSqlContainer? _postgreSqlContainer;

	private PostgresServer? _postgresServer;

	private string? _templateDatabase;

	public string? UnavailableReason { get; private set; }

	public bool IsAvailable => UnavailableReason is null;

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

	public Task<ArchiveDatabase> CreateEmptyDatabaseAsync(CancellationToken cancellationToken = default)
	{
		return ArchiveDatabase.EmptyAsync(Server, _creationGate, cancellationToken);
	}

	public void RequireAvailable()
	{
		DatabaseGate.Require(UnavailableReason, TestEnvironment.DatabaseRequired);
	}

	public async ValueTask InitializeAsync()
	{
		var semibase = SemibaseBinary.Resolve();

		if (semibase.IsFailed)
		{
			UnavailableReason = semibase.Errors[0].Message;

			return;
		}

		var server = TestEnvironment.TestServerConnectionString is { } connectionString
			? await UseExistingServerAsync(semibase.Value, connectionString)
			: await StartContainerAsync(semibase.Value);

		if (server.IsFailed)
		{
			UnavailableReason = server.Errors[0].Message;

			return;
		}

		var template = await ArchiveTemplate.BuildAsync(server.Value);

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
	// distinction does not matter here: any failure to start is the same unavailable reason.
	private async Task<Result<PostgresServer>> StartContainerAsync(string semibaseExecutable)
	{
		try
		{
			var container = new PostgreSqlBuilder(TestEnvironment.Image)
				.WithUsername(SuperuserName)
				.WithPassword(ContainerSuperuserPassword)
				.WithDatabase(MaintenanceDatabase)
				.Build();

			await container.StartAsync();

			_postgreSqlContainer = container;

			return Result.Ok(
				new PostgresServer(
					semibaseExecutable,
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
				$"no container runtime started {TestEnvironment.Image}: {exception.Message}");
		}
	}

	// The named server must itself be semibase-provisioned; this path only confirms it answers, since a
	// server that refuses a connection is as unavailable as a missing container runtime. The fixture
	// still re-runs `semibase create` against it, which is idempotent, so it needs the superuser
	// password in the connection string and the two role passwords in the environment — a real server
	// carries real roles, and inventing passwords for them would change them.
	private static async Task<Result<PostgresServer>> UseExistingServerAsync(
		string semibaseExecutable,
		string connectionString)
	{
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

		return Result.Ok(
			new PostgresServer(
				semibaseExecutable,
				builder.Host ?? "localhost",
				builder.Port,
				builder.Username ?? SuperuserName,
				builder.Password ?? string.Empty,
				builder.Database ?? MaintenanceDatabase,
				writerPassword,
				readerPassword));
	}
}
