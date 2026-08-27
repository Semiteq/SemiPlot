using System.Reflection;

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
	// Both namespaces that hold a startup error type, not only the Core one. StartupReadTimedOutError is
	// UI-local — Task.WaitAsync gives up on a read before the probe knows a host — and enumerating only
	// SemiPlot.Core.Data.Errors would let it, and every UI-local type after it, reach the operator through
	// the catch-all with no test failing.
	private static readonly IReadOnlyList<Type> _errorTypes = CollectErrorTypes();

	[Fact]
	public void EveryPublicErrorType_MapsToItsOwnState()
	{
		var unmapped = _errorTypes
			.Where(type => StartupFailureMapper.Map(Instantiate(type)).Title == StartupFailureMapper.GenericTitle)
			.Select(type => type.FullName)
			.ToList();

		unmapped.Should().BeEmpty(
			"every public IError must have an arm in StartupFailureMapper; the catch-all is not a mapping");
	}

	[Fact]
	public void ErrorTypeEnumeration_CoversBothNamespaces()
	{
		// A coverage test over an empty set passes vacuously. This pins that the reflection actually finds
		// the vocabulary, Core's ten types and the UI-local one.
		_errorTypes.Should().HaveCount(11);
		_errorTypes.Should().Contain(typeof(ArchiveReadFailedError)).And.Contain(typeof(StartupReadTimedOutError));
	}

	[Fact]
	public void ConnectionFileNotFound_SendsTheOperatorToTheFile()
	{
		var view = StartupFailureMapper.Map(new ConnectionFileNotFoundError(@"C:\DISTR\Config\SemiPlot\a.yaml"));

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
	[InlineData(ConnectionFileProblem.VersionMismatch, "format version")]
	public void ConnectionFileInvalid_RemedyFollowsTheProblem(ConnectionFileProblem kind, string expectedPhrase)
	{
		var view = StartupFailureMapper.Map(new ConnectionFileInvalidError("a.yaml", kind, "the reason"));

		view.Title.Should().Be("Connection file cannot be read");
		view.Detail.Should().Contain("the reason");
		view.Remedy.Should().Contain(expectedPhrase);
	}

	[Fact]
	public void ArchiveUnreachable_SendsTheOperatorToTheNetwork()
	{
		var view = StartupFailureMapper.Map(new ArchiveUnreachableError("scada-host", 5432, "semiplot"));

		view.Title.Should().Be("No connection to the archive");
		view.Detail.Should().Contain("scada-host:5432");
		view.Remedy.Should().Contain("firewall");
	}

	[Fact]
	public void ArchiveAccessDenied_SendsTheOperatorToTheCredentials()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveAccessDeniedError("scada-host", 5432, "semiplot", "scada_reader"));

		view.Title.Should().Be("The archive refused the credentials");
		view.Detail.Should().Contain("scada_reader");
		view.Remedy.Should().Contain("password");
		view.Remedy.Should().Contain("SELECT");
	}

	[Fact]
	public void ArchiveNotInitialised_MissingDatabase_SendsTheOperatorToSemibaseSite()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveNotInitialisedError("scada-host", 5432, "semiplot", ArchiveObject.Database, null));

		view.Title.Should().Be("The archive is not provisioned");
		view.Detail.Should().Contain("holds no database 'semiplot'");
		view.Remedy.Should().Contain("semibase site");
	}

	[Fact]
	public void ArchiveNotInitialised_MissingTrends_SendsTheOperatorToSemibaseSite()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveNotInitialisedError("scada-host", 5432, "semiplot", ArchiveObject.Table, "trends"));

		view.Detail.Should().Contain("holds no table 'trends'");
		view.Remedy.Should().Contain("trends");
		view.Remedy.Should().Contain("semibase site");
	}

	[Fact]
	public void ArchiveNotInitialised_MissingTagTable_SendsTheOperatorToSemibaseSite()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveNotInitialisedError("scada-host", 5432, "semiplot", ArchiveObject.Table, "semiplot_tags"));

		view.Remedy.Should().Contain("semiplot_tags");
		view.Remedy.Should().Contain("semibase site");
	}

	// Both tables arrive from the same provisioning run, so the remedy may not branch on which one is
	// absent. Substituting the table name out of each remedy leaves two strings that must be equal: any
	// arm switching on the table name makes them differ, whatever the arm says.
	[Fact]
	public void ArchiveNotInitialised_TheRemedyDoesNotDependOnWhichTableIsAbsent()
	{
		var trendsRemedy = RemedyWithTableNameElided("trends");
		var tagTableRemedy = RemedyWithTableNameElided("semiplot_tags");

		trendsRemedy.Should().Be(tagTableRemedy);
	}

	private static string RemedyWithTableNameElided(string table)
	{
		var view = StartupFailureMapper.Map(
			new ArchiveNotInitialisedError("scada-host", 5432, "semiplot", ArchiveObject.Table, table));

		return view.Remedy.Replace(table, "<table>", StringComparison.Ordinal);
	}

	[Fact]
	public void ArchiveQueryTimedOut_WithABound_NamesIt()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveQueryTimedOutError("scada-host", 5432, "semiplot", TimeSpan.FromSeconds(30)));

		view.Title.Should().Be("The archive ended the read");
		view.Detail.Should().Contain("30 s");
		view.Remedy.Should().Contain("statement_timeout");
	}

	[Fact]
	public void ArchiveQueryTimedOut_WithoutABound_DoesNotInventOne()
	{
		var view = StartupFailureMapper.Map(
			new ArchiveQueryTimedOutError("scada-host", 5432, "semiplot", TimeSpan.Zero));

		view.Detail.Should().Contain("57014");
		view.Detail.Should().NotContain("0 s");
		view.Remedy.Should().Contain("cancelled");
	}

	[Fact]
	public void ArchiveReadFailed_WithASqlState_NamesItForTheReport()
	{
		var view = StartupFailureMapper.Map(new ArchiveReadFailedError("scada-host", 5432, "semiplot", "22003"));

		view.Title.Should().Be("The archive rejected the read");
		view.Detail.Should().Contain("SQLSTATE 22003");
		view.Remedy.Should().Contain("PostgreSQL server log");
	}

	[Fact]
	public void ArchiveReadFailed_WithoutASqlState_PointsAtTheClientSide()
	{
		var view = StartupFailureMapper.Map(new ArchiveReadFailedError("scada-host", 5432, "semiplot", string.Empty));

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
			new ArchiveConnectionLostError("bench.example", 5432, "semiplot_dev", 3));

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
		var view = StartupFailureMapper.Map(new ArchiveShapeUnexpectedError(
			"scada-host",
			5432,
			"semiplot",
			"column \"v\" does not exist"));

		view.Title.Should().Be("The archive has an unexpected shape");
		view.Detail.Should().Contain("scada-host:5432").And.Contain("column \"v\" does not exist");
		view.Remedy.Should().Contain("public.trends").And.Contain("semibase site");
	}

	// It never reaches a window: the health check carries it out beside a successful read and the banner
	// draws it over a working chart. The arm is here because the coverage test enumerates the vocabulary by
	// namespace, and a type with no arm would reach the operator through the catch-all instead.
	[Fact]
	public void ArchiveDefaultPartitionNotEmpty_NamesThePartitionAndSendsTheOperatorToTheScada()
	{
		var view = StartupFailureMapper.Map(new ArchiveDefaultPartitionNotEmptyError(
			"scada-host",
			5432,
			"semiplot",
			"public.tpdefault"));

		view.Title.Should().Be("The archive's default partition holds rows");
		view.Detail.Should().Contain("scada-host:5432").And.Contain("public.tpdefault");
		view.Remedy.Should().Contain("SCADA");
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

	private static IReadOnlyList<Type> CollectErrorTypes()
	{
		var namespaces = new[]
		{
			(Assembly: typeof(ArchiveUnreachableError).Assembly, Name: typeof(ArchiveUnreachableError).Namespace),
			(Assembly: typeof(StartupProbe).Assembly, Name: typeof(StartupReadTimedOutError).Namespace)
		};

		return namespaces
			.SelectMany(source => source.Assembly.GetExportedTypes()
				.Where(type => type.Namespace == source.Name)
				.Where(type => type is { IsClass: true, IsAbstract: false })
				.Where(type => typeof(IError).IsAssignableFrom(type)))
			.OrderBy(type => type.FullName, StringComparer.Ordinal)
			.ToList();
	}

	// Every error type in the vocabulary takes only value-like constructor parameters, so a synthetic
	// instance needs no factory per type — the coverage test routes on the type, never on the values.
	private static IError Instantiate(Type type)
	{
		var constructor = type.GetConstructors().Single();

		var arguments = constructor.GetParameters()
			.Select(parameter => SampleValue(parameter.ParameterType))
			.ToArray();

		return (IError)constructor.Invoke(arguments);
	}

	private static object? SampleValue(Type parameterType)
	{
		if (parameterType == typeof(string))
		{
			return "sample";
		}

		if (parameterType == typeof(int))
		{
			return 5432;
		}

		if (parameterType == typeof(TimeSpan))
		{
			return TimeSpan.FromSeconds(1);
		}

		if (parameterType.IsEnum)
		{
			return Enum.GetValues(parameterType).GetValue(0);
		}

		throw new NotSupportedException(
			$"No sample value for constructor parameter type '{parameterType}'. Extend SampleValue.");
	}
}
