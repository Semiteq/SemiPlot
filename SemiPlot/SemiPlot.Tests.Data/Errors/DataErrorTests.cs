using FluentResults;

using SemiPlot.Core.Data.Errors;

using Xunit;

namespace SemiPlot.Tests.Data.Errors;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DataErrorTests
{
	private const string ConnectionFilePath = @"C:\etc\semiplot\archive-connection.yaml";
	private const string Host = "scada-01";
	private const int Port = 5432;
	private const string Database = "semiplot_dev";

	[Fact]
	public void ConnectionFileNotFoundErrorCarriesThePath()
	{
		var error = new ConnectionFileNotFoundError(ConnectionFilePath);

		Assert.Equal(ConnectionFilePath, error.Path);
	}

	[Theory]
	[InlineData(ConnectionFileProblem.Unreadable)]
	[InlineData(ConnectionFileProblem.Unparseable)]
	[InlineData(ConnectionFileProblem.MissingField)]
	[InlineData(ConnectionFileProblem.OutOfRange)]
	[InlineData(ConnectionFileProblem.UnknownTimeZone)]
	[InlineData(ConnectionFileProblem.VersionMismatch)]
	public void ConnectionFileInvalidErrorKeepsItsDiscriminator(ConnectionFileProblem kind)
	{
		const string reason = "source_time_zone is blank";

		var error = new ConnectionFileInvalidError(ConnectionFilePath, kind, reason);

		Assert.Equal(ConnectionFilePath, error.Path);
		Assert.Equal(kind, error.Kind);
		Assert.Equal(reason, error.Reason);
	}

	[Fact]
	public void ArchiveUnreachableErrorCarriesTheEndpoint()
	{
		var error = new ArchiveUnreachableError(Host, Port, Database);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
	}

	[Fact]
	public void ArchiveAccessDeniedErrorCarriesTheEndpointAndTheUser()
	{
		const string username = "semiplot_reader";

		var error = new ArchiveAccessDeniedError(Host, Port, Database, username);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Equal(username, error.Username);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void ArchiveNotInitialisedErrorCarriesTheMissingTable(string table)
	{
		var error = new ArchiveNotInitialisedError(Host, Port, Database, ArchiveObject.Table, table);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Equal(ArchiveObject.Table, error.MissingObject);
		Assert.Equal(table, error.Table);
	}

	// The consumer routes on Table once MissingObject says a table is absent, and a message reading
	// "holds no table ''" names nothing to act on. So the pair is rejected where it is built.
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void ArchiveNotInitialisedErrorRejectsATableCaseNamingNoTable(string? table)
	{
		Assert.ThrowsAny<ArgumentException>(
			() => new ArchiveNotInitialisedError(Host, Port, Database, ArchiveObject.Table, table));
	}

	[Fact]
	public void ArchiveNotInitialisedErrorCarriesAMissingDatabaseWithNoTable()
	{
		var error = new ArchiveNotInitialisedError(Host, Port, Database, ArchiveObject.Database, table: null);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Equal(ArchiveObject.Database, error.MissingObject);
		Assert.Null(error.Table);
	}

	[Fact]
	public void ArchiveQueryTimedOutErrorCarriesTheEffectiveBoundAndTheEndpoint()
	{
		var timeout = TimeSpan.FromSeconds(30);

		var error = new ArchiveQueryTimedOutError(Host, Port, Database, timeout);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Equal(timeout, error.Timeout);
	}

	[Fact]
	public void ArchiveReadFailedErrorCarriesTheEndpointAndTheSqlState()
	{
		const string sqlState = "42P07";

		var error = new ArchiveReadFailedError(Host, Port, Database, sqlState);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Equal(sqlState, error.SqlState);
	}

	[Fact]
	public void ArchiveReadFailedErrorReadsWithoutASqlState()
	{
		var error = new ArchiveReadFailedError(Host, Port, Database, string.Empty);

		Assert.Equal(string.Empty, error.SqlState);
	}

	[Fact]
	public void EachArchiveStateStaysTellableApartThroughAFailedResult()
	{
		var unreachable = Result.Fail(new ArchiveUnreachableError(Host, Port, Database));
		var databaseMissing = Result.Fail(
			new ArchiveNotInitialisedError(Host, Port, Database, ArchiveObject.Database, table: null));
		var accessDenied = Result.Fail(new ArchiveAccessDeniedError(Host, Port, Database, "semiplot_reader"));
		var tableMissing = Result.Fail(
			new ArchiveNotInitialisedError(Host, Port, Database, ArchiveObject.Table, "trends"));
		var readFailed = Result.Fail(new ArchiveReadFailedError(Host, Port, Database, "42P07"));

		Assert.Single(unreachable.Errors.OfType<ArchiveUnreachableError>());
		Assert.Empty(unreachable.Errors.OfType<ArchiveNotInitialisedError>());
		Assert.Equal(
			ArchiveObject.Database,
			Assert.Single(databaseMissing.Errors.OfType<ArchiveNotInitialisedError>()).MissingObject);
		Assert.Single(accessDenied.Errors.OfType<ArchiveAccessDeniedError>());
		Assert.Empty(accessDenied.Errors.OfType<ArchiveUnreachableError>());
		Assert.Equal(
			ArchiveObject.Table,
			Assert.Single(tableMissing.Errors.OfType<ArchiveNotInitialisedError>()).MissingObject);
		Assert.Single(readFailed.Errors.OfType<ArchiveReadFailedError>());
		Assert.Empty(readFailed.Errors.OfType<ArchiveNotInitialisedError>());
	}

	[Fact]
	public void EveryPublicErrorTypeIsSealedAndDerivesFromError()
	{
		var errorTypes = typeof(ConnectionFileNotFoundError).Assembly
			.GetExportedTypes()
			.Where(type => type.Namespace == typeof(ConnectionFileNotFoundError).Namespace)
			.Where(type => type.IsClass)
			.ToList();

		Assert.NotEmpty(errorTypes);
		Assert.All(errorTypes, type => Assert.True(type.IsSealed, type.Name));
		Assert.All(errorTypes, type => Assert.True(typeof(IError).IsAssignableFrom(type), type.Name));
		Assert.All(errorTypes, type => Assert.EndsWith("Error", type.Name, StringComparison.Ordinal));
	}
}
