using System.Globalization;

using FluentResults;

using SemiPlot.Core.Data;
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
		Assert.Contains(ConnectionFilePath, error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(ConnectionFileProblem.Unreadable)]
	[InlineData(ConnectionFileProblem.Unparseable)]
	[InlineData(ConnectionFileProblem.MissingField)]
	[InlineData(ConnectionFileProblem.OutOfRange)]
	[InlineData(ConnectionFileProblem.UnknownTimeZone)]
	public void ConnectionFileInvalidErrorKeepsItsDiscriminator(ConnectionFileProblem kind)
	{
		const string reason = "source_time_zone is blank";

		var error = new ConnectionFileInvalidError(ConnectionFilePath, kind, reason);

		Assert.Equal(ConnectionFilePath, error.Path);
		Assert.Equal(kind, error.Kind);
		Assert.Equal(reason, error.Reason);
		Assert.Contains(ConnectionFilePath, error.Message, StringComparison.Ordinal);
		Assert.Contains(reason, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ConnectionFileVersionMismatchErrorCarriesBothVersions()
	{
		var error = new ConnectionFileVersionMismatchError(ConnectionFilePath, "2", "1");

		Assert.Equal(ConnectionFilePath, error.Path);
		Assert.Equal("2", error.FoundVersion);
		Assert.Equal("1", error.ExpectedVersion);
		Assert.Contains("2", error.Message, StringComparison.Ordinal);
		Assert.Contains("1", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ArchiveUnreachableErrorCarriesTheEndpoint()
	{
		var error = new ArchiveUnreachableError(Host, Port, Database);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Contains(Host, error.Message, StringComparison.Ordinal);
		Assert.Contains(Port.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
		Assert.Contains(Database, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ArchiveDatabaseMissingErrorCarriesTheEndpoint()
	{
		var error = new ArchiveDatabaseMissingError(Host, Port, Database);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Contains(Host, error.Message, StringComparison.Ordinal);
		Assert.Contains(Port.ToString(CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
		Assert.Contains(Database, error.Message, StringComparison.Ordinal);
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
		Assert.Contains(username, error.Message, StringComparison.Ordinal);
		Assert.Contains(Database, error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void ArchiveNotInitialisedErrorCarriesTheMissingTable(string table)
	{
		var error = new ArchiveNotInitialisedError(Host, Port, Database, table);

		Assert.Equal(Host, error.Host);
		Assert.Equal(Port, error.Port);
		Assert.Equal(Database, error.Database);
		Assert.Equal(table, error.Table);
		Assert.Contains(table, error.Message, StringComparison.Ordinal);
		Assert.Contains(Database, error.Message, StringComparison.Ordinal);
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
		Assert.Contains("30", error.Message, StringComparison.Ordinal);
		Assert.Contains(Database, error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ProviderNotImplementedErrorCarriesTheMemberName()
	{
		var error = new ProviderNotImplementedError(nameof(IDataProvider.QueryHistoryAsync));

		Assert.Equal("QueryHistoryAsync", error.MemberName);
		Assert.Contains("QueryHistoryAsync", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void EachArchiveStateStaysTellableApartThroughAFailedResult()
	{
		var unreachable = Result.Fail(new ArchiveUnreachableError(Host, Port, Database));
		var databaseMissing = Result.Fail(new ArchiveDatabaseMissingError(Host, Port, Database));
		var accessDenied = Result.Fail(new ArchiveAccessDeniedError(Host, Port, Database, "semiplot_reader"));
		var notInitialised = Result.Fail(new ArchiveNotInitialisedError(Host, Port, Database, "trends"));

		Assert.Single(unreachable.Errors.OfType<ArchiveUnreachableError>());
		Assert.Empty(unreachable.Errors.OfType<ArchiveDatabaseMissingError>());
		Assert.Single(databaseMissing.Errors.OfType<ArchiveDatabaseMissingError>());
		Assert.Single(accessDenied.Errors.OfType<ArchiveAccessDeniedError>());
		Assert.Empty(accessDenied.Errors.OfType<ArchiveUnreachableError>());
		Assert.Single(notInitialised.Errors.OfType<ArchiveNotInitialisedError>());
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
