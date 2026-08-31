using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.UI.Startup;

// The mapper is a pure function over IError and touches no Avalonia type, so these are plain [Fact].
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class StartupFailureMapperTests
{
	// A coverage test over the enums rather than over reflected types: the two error types are closed
	// over their discriminators, so a new member without an arm reaches the operator through the
	// catch-all, and this is what fails when it does.
	[Theory]
	[MemberData(nameof(ArchiveFaults))]
	public void EveryArchiveFault_MapsToItsOwnState(ArchiveFault kind)
	{
		var view = StartupFailureMapper.Map(new ArchiveError(kind, "scada-host", 5432, "semiplot", "sample"));

		view.Title.Should().NotBe(StartupFailureMapper.GenericTitle);
	}

	[Theory]
	[MemberData(nameof(ConnectionFileProblems))]
	public void EveryConnectionFileProblem_MapsToItsOwnState(ConnectionFileProblem kind)
	{
		var view = StartupFailureMapper.Map(new ConnectionFileError("a.yaml", kind, "sample"));

		view.Title.Should().NotBe(StartupFailureMapper.GenericTitle);
	}

	public static TheoryData<ArchiveFault> ArchiveFaults => new(Enum.GetValues<ArchiveFault>());

	public static TheoryData<ConnectionFileProblem> ConnectionFileProblems =>
		new(Enum.GetValues<ConnectionFileProblem>());

	[Fact]
	public void ConnectionFileNotFound_SendsTheOperatorToTheFile()
	{
		var view = StartupFailureMapper.Map(
			new ConnectionFileError(@"C:\DISTR\Config\SemiPlot\a.yaml", ConnectionFileProblem.NotFound));

		view.Title.Should().Be("Connection file not found");
		view.Detail.Should().Contain(@"C:\DISTR\Config\SemiPlot\a.yaml");
		view.Remedy.Should().Contain("--config-dir");
	}

	[Theory]
	[InlineData(ConnectionFileProblem.Unreadable, "read access")]
	[InlineData(ConnectionFileProblem.Unparseable, "YAML syntax")]
	[InlineData(ConnectionFileProblem.MissingField, "Add the field")]
	[InlineData(ConnectionFileProblem.OutOfRange, "inside the range")]
	[InlineData(ConnectionFileProblem.UnknownTimeZone, "IANA identifier")]
	public void ConnectionFileInvalid_RemedyFollowsTheProblem(ConnectionFileProblem kind, string expectedPhrase)
	{
		var view = StartupFailureMapper.Map(new ConnectionFileError("a.yaml", kind, "the reason"));

		view.Title.Should().Be("Connection file cannot be read");
		view.Detail.Should().Contain("the reason");
		view.Remedy.Should().Contain(expectedPhrase);
	}

	[Fact]
	public void ArchiveUnreachable_SendsTheOperatorToTheNetwork()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.Unreachable));

		view.Title.Should().Be("No connection to the archive");
		view.Detail.Should().Contain("scada-host:5432");
		view.Remedy.Should().Contain("firewall");
	}

	[Fact]
	public void ArchiveAccessDenied_SendsTheOperatorToTheCredentials()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.AccessDenied, "scada_reader"));

		view.Title.Should().Be("The archive refused the credentials");
		view.Detail.Should().Contain("scada_reader");
		view.Remedy.Should().Contain("password");
		view.Remedy.Should().Contain("SELECT");
	}

	[Fact]
	public void ArchiveDatabaseMissing_SendsTheOperatorToSemibaseSite()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.DatabaseMissing));

		view.Title.Should().Be("The archive is not provisioned");
		view.Detail.Should().Contain("holds no database 'semiplot'");
		view.Remedy.Should().Contain("semibase site");
	}

	[Theory]
	[InlineData("trends")]
	[InlineData("semiplot_tags")]
	public void ArchiveTableMissing_NamesTheTableAndSendsTheOperatorToSemibaseSite(string table)
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.TableMissing, table));

		view.Title.Should().Be("The archive is not provisioned");
		view.Detail.Should().Contain($"holds no table '{table}'");
		view.Remedy.Should().Contain(table);
		view.Remedy.Should().Contain("semibase site");
	}

	// Both tables arrive from the same provisioning run, so the remedy may not branch on which one is
	// absent. Substituting the table name out of each remedy leaves two strings that must be equal.
	[Fact]
	public void ArchiveTableMissing_TheRemedyDoesNotDependOnWhichTableIsAbsent()
	{
		var trendsRemedy = RemedyWithTableNameElided("trends");
		var tagTableRemedy = RemedyWithTableNameElided("semiplot_tags");

		trendsRemedy.Should().Be(tagTableRemedy);
	}

	private static string RemedyWithTableNameElided(string table)
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.TableMissing, table));

		return view.Remedy.Replace(table, "<table>", StringComparison.Ordinal);
	}

	[Fact]
	public void ArchiveQueryTimedOut_NamesTheSqlStateAndTheReaderRolesBound()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.QueryTimedOut));

		view.Title.Should().Be("The archive ended the read");
		view.Detail.Should().Contain("scada-host:5432").And.Contain("57014");
		view.Remedy.Should().Contain("statement_timeout").And.Contain("cancelled");
	}

	[Fact]
	public void ArchiveReadFailed_WithASqlState_NamesItForTheReport()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.ReadFailed, "22003"));

		view.Title.Should().Be("The archive rejected the read");
		view.Detail.Should().Contain("SQLSTATE 22003");
		view.Remedy.Should().Contain("PostgreSQL server log");
	}

	[Fact]
	public void ArchiveReadFailed_WithoutASqlState_PointsAtTheClientSide()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.ReadFailed));

		view.Detail.Should().Contain("no SQLSTATE");
		view.Remedy.Should().Contain("client side");
	}

	[Fact]
	public void StartupReadTimedOut_SeparatesTheCallersBoundFromTheServers()
	{
		var view = StartupFailureMapper.Map(
			new StartupReadTimedOutError("pen catalogue", TimeSpan.FromSeconds(15)));

		view.Title.Should().Be("The archive did not answer in time");
		view.Detail.Should().Contain("pen catalogue").And.Contain("15 s");
		view.Remedy.Should().Contain("host and port are right");
	}

	// A lost live edge is drawn as a banner over a chart that keeps its history, so the words say what is
	// still true as well as what failed.
	[Fact]
	public void ArchiveConnectionLost_NamesTheRunAndLeavesTheHistoryStanding()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveError(ArchiveFault.ConnectionLost, "bench.example", 5432, "semiplot_dev", "3"));

		view.Title.Should().Be("The archive stopped answering");
		view.Detail.Should().Contain("semiplot_dev").And.Contain("bench.example:5432").And.Contain("3");
		view.Detail.Should().Contain("history already drawn is unaffected");
		view.Remedy.Should().Contain("keeps polling");
	}

	// The words stop where the knowledge stops: no shape is held on this side, so the detail quotes the
	// server and the remedy points at the provisioning that owns the table.
	[Fact]
	public void ArchiveShapeUnexpected_QuotesTheServerAndSendsTheOperatorToTheProvisioning()
	{
		var view = StartupFailureMapper.Map(Archive(ArchiveFault.ShapeUnexpected, "column \"v\" does not exist"));

		view.Title.Should().Be("The archive has an unexpected shape");
		view.Detail.Should().Contain("scada-host:5432").And.Contain("column \"v\" does not exist");
		view.Remedy.Should().Contain("public.trends").And.Contain("semibase site");
	}

	// The exception arm is what stops a throw on the startup path — a data source that cannot be built, a
	// cancelled read — from ending the process with no window at all.
	[Fact]
	public void ThrownException_NamesItsTypeInsteadOfExitingSilently()
	{
		var view = StartupFailureMapper.Map(
			new ExceptionalError("no data source", new InvalidOperationException("no data source")));

		view.Title.Should().Be("Startup failed unexpectedly");
		view.Detail.Should().Contain(nameof(InvalidOperationException)).And.Contain("no data source");
		view.Remedy.Should().Contain("log file");
	}

	[Fact]
	public void UnknownError_FallsToTheGenericState()
	{
		var view = StartupFailureMapper.Map(new Error("something this build never named"));

		view.Title.Should().Be(StartupFailureMapper.GenericTitle);
		view.Detail.Should().Be("something this build never named");
	}

	private static ArchiveError Archive(ArchiveFault kind, string detail = "")
	{
		return new ArchiveError(kind, "scada-host", 5432, "semiplot", detail);
	}
}
