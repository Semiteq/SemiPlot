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
