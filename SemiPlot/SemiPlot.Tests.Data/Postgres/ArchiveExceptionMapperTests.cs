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
	public void ASocketFailureMapsToUnreachable()
	{
		var exception = new NpgsqlException("failed to connect", new SocketException(10061));

		var error = Map(exception);

		Assert.Equal(ArchiveFault.Unreachable, error.Kind);
		AssertEndpoint(error);
	}

	[Fact]
	public void TheCommandBoundFiringMapsToUnreachableAndNotToQueryTimedOut()
	{
		var exception = new NpgsqlException("exception while reading from stream", new TimeoutException());

		Assert.Equal(ArchiveFault.Unreachable, Map(exception).Kind);
	}

	[Fact]
	public void AMissingDatabaseMapsToDatabaseMissingAndNamesNoTable()
	{
		var error = Map(Postgres("3D000"));

		Assert.Equal(ArchiveFault.DatabaseMissing, error.Kind);
		Assert.Equal(string.Empty, error.Detail);
		AssertEndpoint(error);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void AnUndefinedTableReportsTheRelationTheCallerResolved(string relation)
	{
		var error = Map(Postgres("42P01"), relation);

		Assert.Equal(ArchiveFault.TableMissing, error.Kind);
		Assert.Equal(relation, error.Detail);
		AssertEndpoint(error);
	}

	[Theory]
	[InlineData("28P01")]
	[InlineData("28000")]
	[InlineData("42501")]
	public void ARejectedCredentialOrAMissingGrantMapsToAccessDenied(string sqlState)
	{
		var error = Map(Postgres(sqlState));

		Assert.Equal(ArchiveFault.AccessDenied, error.Kind);
		Assert.Equal(Username, error.Detail);
		AssertEndpoint(error);
	}

	[Fact]
	public void AServerCancelMapsToQueryTimedOut()
	{
		var error = Map(Postgres("57014"));

		Assert.Equal(ArchiveFault.QueryTimedOut, error.Kind);
		AssertEndpoint(error);
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
	public void AnUndefinedColumnMapsToShapeUnexpectedCarryingTheServersMessage()
	{
		var error = Map(Postgres("42703"));

		Assert.Equal(ArchiveFault.ShapeUnexpected, error.Kind);
		Assert.Equal("the server said so", error.Detail);
		Assert.Contains("the server said so", error.Message, StringComparison.Ordinal);
		AssertEndpoint(error);
	}

	[Fact]
	public void AnUnmappedSqlStateProducesReadFailedCarryingTheSqlState()
	{
		var result = Result.Fail(Map(Postgres("42P07")));

		Assert.True(result.IsFailed);

		var readFailed = Assert.Single(result.Errors.OfType<ArchiveError>());
		Assert.Equal(ArchiveFault.ReadFailed, readFailed.Kind);
		Assert.Equal("42P07", readFailed.Detail);
		AssertEndpoint(readFailed);
	}

	[Fact]
	public void AClientSideFailureCarriesNoSqlStateAndStillCrossesTyped()
	{
		var error = Map(new InvalidCastException("column is int4"));

		Assert.Equal(ArchiveFault.ReadFailed, error.Kind);
		Assert.Equal(string.Empty, error.Detail);
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

	private static ArchiveError Map(Exception exception, string? relation = null)
	{
		var mapper = new ArchiveExceptionMapper(ConnectionSettingsFactory.Create(host: Host, port: Port));

		return Assert.IsType<ArchiveError>(mapper.Map(exception, relation));
	}

	private static void AssertEndpoint(ArchiveError error)
	{
		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
	}
}
