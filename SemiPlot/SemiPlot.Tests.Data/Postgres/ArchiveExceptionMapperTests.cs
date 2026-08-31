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
	public void AMissingDatabaseMapsToTheDatabaseDiscriminatorAndNamesNoTable()
	{
		var error = Map(Postgres("3D000"));

		var missing = Assert.IsType<ArchiveNotInitialisedError>(error);
		Assert.Equal(ArchiveObject.Database, missing.MissingObject);
		Assert.Null(missing.Table);
		AssertEndpoint(missing.Host, missing.Port, missing.Database);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void AnUndefinedTableReportsTheRelationTheCallerResolved(string relation)
	{
		var error = Map(Postgres("42P01"), relation);

		var notInitialised = Assert.IsType<ArchiveNotInitialisedError>(error);
		Assert.Equal(ArchiveObject.Table, notInitialised.MissingObject);
		Assert.Equal(relation, notInitialised.Table);
		AssertEndpoint(notInitialised.Host, notInitialised.Port, notInitialised.Database);
	}

	// The two SQLSTATEs now share one type, so the discriminator is the only thing keeping them apart.
	[Fact]
	public void TheTwoAbsentObjectStatesStayApartOnTheDiscriminator()
	{
		var databaseMissing = Assert.IsType<ArchiveNotInitialisedError>(Map(Postgres("3D000")));
		var tableMissing = Assert.IsType<ArchiveNotInitialisedError>(Map(Postgres("42P01"), "trends"));

		Assert.NotEqual(databaseMissing.MissingObject, tableMissing.MissingObject);
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
	public void AServerCancelMapsToTheTimedOutError()
	{
		var timedOut = Assert.IsType<ArchiveQueryTimedOutError>(Map(Postgres("57014")));

		AssertEndpoint(timedOut.Host, timedOut.Port, timedOut.Database);
	}

	[Fact]
	public void ACancelledOperationIsRethrownRatherThanMapped()
	{
		var exception = new OperationCanceledException("the caller asked");

		var thrown = Assert.Throws<OperationCanceledException>(() => Map(exception));

		Assert.Same(exception, thrown);
	}

	// 42703 is how a trends whose columns have moved reaches this build: a real read names a column the
	// server cannot resolve. Nothing probes the shape up front, so the server's own message text is the
	// only thing that can name the column, and the mapper carries it through unchanged.
	[Fact]
	public void AnUndefinedColumnMapsToTheShapeUnexpectedErrorCarryingTheServersMessage()
	{
		var error = Map(Postgres("42703"));

		var shape = Assert.IsType<ArchiveShapeUnexpectedError>(error);
		Assert.Equal("the server said so", shape.Detail);
		Assert.Contains("the server said so", shape.Message, StringComparison.Ordinal);
		AssertEndpoint(shape.Host, shape.Port, shape.Database);
	}

	// The unmapped example stays unmapped: adding the 42703 arm must not widen the read-failed arm's
	// catch-all into anything else the tests already stand on.
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
	[InlineData("42703")]
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

	private static Error Map(Exception exception, string? relation = null)
	{
		var mapper = new ArchiveExceptionMapper(ConnectionSettingsFactory.Create(host: Host, port: Port));

		return mapper.Map(exception, relation);
	}

	private static void AssertEndpoint(string host, int port, string database)
	{
		Assert.Equal(Host, host);
		Assert.Equal(Port, port);
		Assert.Equal(Database, database);
	}
}
