using System.Net.Sockets;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// The mapper opens no connection and issues no query, so every case here runs over a fabricated
// exception and a settings instance. It is also the only coverage of the two states no gated test can
// reach — nothing answering at the configured address, and the database not existing — because the
// harness always hands out a reachable server holding the database it created.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveExceptionMapperTests
{
	private const string Host = "scada-01";
	private const int Port = 5432;
	private const string Database = ConnectionSettingsFactory.Database;
	private const string Username = ConnectionSettingsFactory.Username;

	private static readonly TimeSpan _effectiveBound = TimeSpan.FromSeconds(30);

	[Fact]
	public void ASocketFailureMapsToTheUnreachableError()
	{
		var exception = new NpgsqlException("failed to connect", new SocketException(10061));

		var error = Map(exception);

		var unreachable = Assert.IsType<ArchiveUnreachableError>(error);
		AssertEndpoint(unreachable.Host, unreachable.Port, unreachable.Database);
	}

	[Fact]
	public void TheCommandBoundFiringMapsToTheUnreachableErrorAndNotToTheTimedOutError()
	{
		var exception = new NpgsqlException("exception while reading from stream", new TimeoutException());

		var error = Map(exception);

		Assert.IsType<ArchiveUnreachableError>(error);
	}

	[Fact]
	public void AMissingDatabaseMapsToTheDatabaseMissingError()
	{
		var error = Map(Postgres("3D000"));

		var missing = Assert.IsType<ArchiveDatabaseMissingError>(error);
		AssertEndpoint(missing.Host, missing.Port, missing.Database);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void AnUndefinedTableReportsTheRelationTheCallerResolved(string relation)
	{
		var error = Map(Postgres("42P01"), relation);

		var notInitialised = Assert.IsType<ArchiveNotInitialisedError>(error);
		Assert.Equal(relation, notInitialised.Table);
		AssertEndpoint(notInitialised.Host, notInitialised.Port, notInitialised.Database);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void AnUndefinedTableWithNoResolvedRelationIsACallerDefect(string? relation)
	{
		Assert.ThrowsAny<ArgumentException>(() => Map(Postgres("42P01"), relation));
	}

	[Theory]
	[InlineData("28P01")]
	[InlineData("28000")]
	[InlineData("42501")]
	public void ARejectedCredentialOrAMissingGrantMapsToAccessDenied(string sqlState)
	{
		var error = Map(Postgres(sqlState));

		var denied = Assert.IsType<ArchiveAccessDeniedError>(error);
		Assert.Equal(Username, denied.Username);
		AssertEndpoint(denied.Host, denied.Port, denied.Database);
	}

	[Fact]
	public void AServerCancelMapsToTheTimedOutErrorCarryingTheBoundTheCallerResolved()
	{
		var error = Map(Postgres("57014"), effectiveBound: _effectiveBound);

		var timedOut = Assert.IsType<ArchiveQueryTimedOutError>(error);
		Assert.Equal(_effectiveBound, timedOut.Timeout);
		AssertEndpoint(timedOut.Host, timedOut.Port, timedOut.Database);
	}

	// The caller passes null when the bound could not be read, which is the reader's own failure answer.
	// This pins that the mapper still produces a usable error rather than a null reference.
	[Fact]
	public void AServerCancelWithAnUnreadableBoundReportsAZeroBoundRatherThanThrowing()
	{
		var timedOut = Assert.IsType<ArchiveQueryTimedOutError>(Map(Postgres("57014")));

		Assert.Equal(TimeSpan.Zero, timedOut.Timeout);
		AssertEndpoint(timedOut.Host, timedOut.Port, timedOut.Database);
	}

	// The one place the wording itself is the property under test. A zero bound covers two states the code
	// cannot tell apart — the bound could not be read, or the server bounds nothing — so the sentence names
	// the SQLSTATE and no number. It must not name statement_timeout: on the second state that setting reads
	// 0, and blaming it sends the operator after a setting that is working as configured.
	[Fact]
	public void AServerCancelWithAnUnreadableBoundNamesTheSqlStateWithoutANumber()
	{
		var timedOut = Assert.IsType<ArchiveQueryTimedOutError>(Map(Postgres("57014")));

		Assert.Contains("57014", timedOut.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("bound of 0", timedOut.Message, StringComparison.Ordinal);
		Assert.DoesNotContain("statement_timeout", timedOut.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ACancelledOperationIsRethrownRatherThanMapped()
	{
		var exception = new OperationCanceledException("the caller asked");

		var thrown = Assert.Throws<OperationCanceledException>(() => Map(exception));

		Assert.Same(exception, thrown);
	}

	[Fact]
	public void AnUnmappedSqlStateProducesAFailedResultCarryingTheReadFailedError()
	{
		var result = Result.Fail(Map(Postgres("42P07")));

		Assert.True(result.IsFailed);

		var readFailed = Assert.Single(result.Errors.OfType<ArchiveReadFailedError>());
		Assert.Equal("42P07", readFailed.SqlState);
		AssertEndpoint(readFailed.Host, readFailed.Port, readFailed.Database);
	}

	[Fact]
	public void AClientSideFailureCarriesNoSqlStateAndStillCrossesTyped()
	{
		var error = Map(new InvalidCastException("column is int4"));

		var readFailed = Assert.IsType<ArchiveReadFailedError>(error);
		Assert.Equal(string.Empty, readFailed.SqlState);
	}

	[Theory]
	[InlineData("3D000")]
	[InlineData("42P01")]
	[InlineData("42501")]
	[InlineData("57014")]
	[InlineData("42P07")]
	public void EveryMappedStateKeepsTheOriginalExceptionAsItsCause(string sqlState)
	{
		var exception = Postgres(sqlState);

		var cause = Assert.Single(Map(exception, "semiplot_tags").Reasons.OfType<ExceptionalError>());

		Assert.Same(exception, cause.Exception);
	}

	private static PostgresException Postgres(string sqlState)
	{
		return new PostgresException("the server said so", "ERROR", "ERROR", sqlState);
	}

	private static Error Map(Exception exception, string? missingRelation = null, TimeSpan? effectiveBound = null)
	{
		var mapper = new ArchiveExceptionMapper(ConnectionSettingsFactory.Create(host: Host, port: Port));

		return mapper.Map(exception, missingRelation, effectiveBound);
	}

	private static void AssertEndpoint(string host, int port, string database)
	{
		Assert.Equal(Host, host);
		Assert.Equal(Port, port);
		Assert.Equal(Database, database);
	}
}
