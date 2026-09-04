using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data.Errors;

using Xunit;

namespace SemiPlot.Tests.Unit.Errors;

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
	[InlineData(ConnectionFileProblem.NotFound)]
	[InlineData(ConnectionFileProblem.Unreadable)]
	[InlineData(ConnectionFileProblem.Unparseable)]
	[InlineData(ConnectionFileProblem.MissingField)]
	[InlineData(ConnectionFileProblem.OutOfRange)]
	[InlineData(ConnectionFileProblem.UnknownTimeZone)]
	public void ConnectionFileErrorKeepsItsDiscriminator(ConnectionFileProblem kind)
	{
		const string Reason = "source_time_zone is blank";

		var error = new ConnectionFileError(ConnectionFilePath, kind, Reason);

		error.Path.Should().Be(ConnectionFilePath);
		error.Kind.Should().Be(kind);
		error.Reason.Should().Be(Reason);
		error.Message.Should().Contain(ConnectionFilePath);
	}

	[Theory]
	[InlineData(ArchiveFault.Unreachable, "")]
	[InlineData(ArchiveFault.AccessDenied, "scada_reader")]
	[InlineData(ArchiveFault.DatabaseMissing, "")]
	[InlineData(ArchiveFault.TableMissing, "trends")]
	[InlineData(ArchiveFault.ShapeUnexpected, "column \"v\" does not exist")]
	[InlineData(ArchiveFault.QueryTimedOut, "")]
	[InlineData(ArchiveFault.ConnectionLost, "3")]
	[InlineData(ArchiveFault.ReadFailed, "42P07")]
	[InlineData(ArchiveFault.ReadFailed, "")]
	public void ArchiveErrorKeepsItsDiscriminatorAndNamesTheAddress(ArchiveFault kind, string detail)
	{
		var error = new ArchiveError(kind, Host, Port, Database, detail);

		error.Kind.Should().Be(kind);
		error.Detail.Should().Be(detail);
		error.Message.Should().Contain(Host);
		error.Message.Should().Contain(Database);
	}

	[Fact]
	public void EveryPublicErrorTypeIsSealedAndDerivesFromError()
	{
		var errorTypes = typeof(ArchiveError).Assembly
			.GetExportedTypes()
			.Where(type => type.Namespace == typeof(ArchiveError).Namespace)
			.Where(type => type.IsClass)
			.ToList();

		errorTypes.Should().NotBeEmpty();
		errorTypes.Should().AllSatisfy(type => type.IsSealed.Should().BeTrue(type.Name));
		errorTypes.Should().AllSatisfy(type => typeof(IError).IsAssignableFrom(type).Should().BeTrue(type.Name));
		errorTypes.Should().AllSatisfy(type => type.Name.Should().EndWith("Error"));
	}
}
