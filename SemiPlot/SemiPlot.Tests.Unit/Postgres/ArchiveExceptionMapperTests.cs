using System.Net.Sockets;

using AwesomeAssertions;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// Runs over a fabricated exception and settings instance; the only coverage of two states no gated
// test can reach, since the harness always hands out a reachable server holding its own database.
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

		error.Kind.Should().Be(ArchiveFault.Unreachable);
		AssertEndpoint(error);
	}

	[Fact]
	public void TheCommandBoundFiringMapsToUnreachableAndNotToQueryTimedOut()
	{
		var exception = new NpgsqlException("exception while reading from stream", new TimeoutException());

		Map(exception).Kind.Should().Be(ArchiveFault.Unreachable);
	}

	[Fact]
	public void AMissingDatabaseMapsToDatabaseMissingAndNamesNoTable()
	{
		var error = Map(Postgres("3D000"));

		error.Kind.Should().Be(ArchiveFault.DatabaseMissing);
		error.Detail.Should().Be(string.Empty);
		AssertEndpoint(error);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void AnUndefinedTableReportsTheRelationTheCallerResolved(string relation)
	{
		var error = Map(Postgres("42P01"), relation);

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be(relation);
		AssertEndpoint(error);
	}

	[Theory]
	[InlineData("28P01")]
	[InlineData("28000")]
	[InlineData("42501")]
	public void ARejectedCredentialOrAMissingGrantMapsToAccessDenied(string sqlState)
	{
		var error = Map(Postgres(sqlState));

		error.Kind.Should().Be(ArchiveFault.AccessDenied);
		error.Detail.Should().Be(Username);
		AssertEndpoint(error);
	}

	[Fact]
	public void AServerCancelMapsToQueryTimedOut()
	{
		var error = Map(Postgres("57014"));

		error.Kind.Should().Be(ArchiveFault.QueryTimedOut);
		AssertEndpoint(error);
	}

	[Fact]
	public void ACancelledOperationIsRethrownRatherThanMapped()
	{
		var exception = new OperationCanceledException("the caller asked");

		var act = () => Map(exception);

		var thrown = act.Should().Throw<OperationCanceledException>().Which;

		thrown.Should().BeSameAs(exception);
	}

	// 42703 is how a trends whose columns have moved reaches this build: a real read names a column the
	// server cannot resolve. Nothing probes the shape up front, so the server's own message text is the
	// only thing that can name the column, and the mapper carries it through unchanged.
	[Fact]
	public void AnUndefinedColumnMapsToShapeUnexpectedCarryingTheServersMessage()
	{
		var error = Map(Postgres("42703"));

		error.Kind.Should().Be(ArchiveFault.ShapeUnexpected);
		error.Detail.Should().Be("the server said so");
		error.Message.Should().Contain("the server said so");
		AssertEndpoint(error);
	}

	[Fact]
	public void AnUnmappedSqlStateProducesReadFailedCarryingTheSqlState()
	{
		var result = Result.Fail(Map(Postgres("42P07")));

		result.IsFailed.Should().BeTrue();

		var readFailed = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;
		readFailed.Kind.Should().Be(ArchiveFault.ReadFailed);
		readFailed.Detail.Should().Be("42P07");
		AssertEndpoint(readFailed);
	}

	[Fact]
	public void AClientSideFailureCarriesNoSqlStateAndStillCrossesTyped()
	{
		var error = Map(new InvalidCastException("column is int4"));

		error.Kind.Should().Be(ArchiveFault.ReadFailed);
		error.Detail.Should().Be(string.Empty);
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

		var cause = Map(exception, "semiplot_tags").Reasons.OfType<ExceptionalError>().Should().ContainSingle().Which;

		cause.Exception.Should().BeSameAs(exception);
	}

	private static PostgresException Postgres(string sqlState)
	{
		return new PostgresException("the server said so", "ERROR", "ERROR", sqlState);
	}

	private static ArchiveError Map(Exception exception, string? relation = null)
	{
		var mapper = new ArchiveExceptionMapper(ConnectionSettingsFactory.Create(host: Host, port: Port));

		return mapper.Map(exception, relation).Should().BeOfType<ArchiveError>().Which;
	}

	private static void AssertEndpoint(ArchiveError error)
	{
		error.Host.Should().Be(Host);
		error.Port.Should().Be(Port);
		error.Database.Should().Be(Database);
	}
}
