using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Messages;

[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class FailureSeverityTests
{
	private static readonly IReadOnlyDictionary<ArchiveFault, MessageSeverity> _archiveFaults =
		new Dictionary<ArchiveFault, MessageSeverity>
		{
			[ArchiveFault.Unreachable] = MessageSeverity.Warning,
			[ArchiveFault.ConnectionLost] = MessageSeverity.Warning,
			[ArchiveFault.QueryTimedOut] = MessageSeverity.Warning,
			[ArchiveFault.ReadFailed] = MessageSeverity.Warning,
			[ArchiveFault.AccessDenied] = MessageSeverity.Error,
			[ArchiveFault.DatabaseMissing] = MessageSeverity.Error,
			[ArchiveFault.TableMissing] = MessageSeverity.Error,
			[ArchiveFault.ShapeUnexpected] = MessageSeverity.Error
		};

	private static readonly IReadOnlyDictionary<SectionProblem, MessageSeverity> _sectionProblems =
		new Dictionary<SectionProblem, MessageSeverity>
		{
			[SectionProblem.DirectoryMissing] = MessageSeverity.Error,
			[SectionProblem.NoFiles] = MessageSeverity.Error,
			[SectionProblem.Unlistable] = MessageSeverity.Error,
			[SectionProblem.Unreadable] = MessageSeverity.Error,
			[SectionProblem.DuplicateKey] = MessageSeverity.Error,
			[SectionProblem.KeyConflict] = MessageSeverity.Error,
			[SectionProblem.Unwritable] = MessageSeverity.Error,
			[SectionProblem.KeyAbsent] = MessageSeverity.Error
		};

	private static readonly IReadOnlyDictionary<AppSettingsProblem, MessageSeverity> _appSettingsProblems =
		new Dictionary<AppSettingsProblem, MessageSeverity>
		{
			[AppSettingsProblem.Unreadable] = MessageSeverity.Error,
			[AppSettingsProblem.KeyMissing] = MessageSeverity.Error,
			[AppSettingsProblem.ValueInvalid] = MessageSeverity.Error
		};

	// One row per arm of ArchiveFailureMapper.Map that is not ArchiveError, keyed by the type the arm
	// matches. The two FluentResults types are the IExceptionalError and the default arm.
	private static readonly IReadOnlyDictionary<Type, (IError Sample, MessageSeverity Severity)> _mapArms =
		new Dictionary<Type, (IError, MessageSeverity)>
		{
			[typeof(StartupArgumentsError)] = (
				new StartupArgumentsError(StartupArgumentsProblem.Missing, "--config-dir"),
				MessageSeverity.Error),
			[typeof(LogFileError)] = (
				new LogFileError(@"Q:\Logs\semiplot.log", "the drive is missing"),
				MessageSeverity.Error),
			[typeof(ConfigurationSectionError)] = (
				new ConfigurationSectionError(ConfigurationSectionName.App, @"C:\config\ui", SectionProblem.NoFiles),
				MessageSeverity.Error),
			[typeof(AppSettingsError)] = (
				new AppSettingsError(@"C:\config\ui", AppSettingsProblem.KeyMissing, "locale"),
				MessageSeverity.Error),
			[typeof(ConnectionFileError)] = (
				new ConnectionFileError(@"C:\config\archive", ConnectionFileProblem.MissingField, "host"),
				MessageSeverity.Error),
			[typeof(StartupReadTimedOutError)] = (
				new StartupReadTimedOutError(StartupRead.PenCatalogue, TimeSpan.FromSeconds(5)),
				MessageSeverity.Error),
			[typeof(ExceptionalError)] = (
				new ExceptionalError(new InvalidOperationException("boom")),
				MessageSeverity.Error),
			[typeof(Error)] = (
				new Error("something this build never named"),
				MessageSeverity.Error)
		};

	// The remedy arm for a connection-file problem falls through to a generic line, so a new member gets no
	// advice and nothing else notices. The other three tables key on switches that throw instead.
	private static readonly IReadOnlyDictionary<ConnectionFileProblem, string> _connectionFileRemedies =
		new Dictionary<ConnectionFileProblem, string>
		{
			[ConnectionFileProblem.Unparseable] = Resources.FailureConnectionFileUnparseableRemedy,
			[ConnectionFileProblem.MissingField] = Resources.FailureConnectionFileMissingFieldRemedy,
			[ConnectionFileProblem.OutOfRange] = Resources.FailureConnectionFileOutOfRangeRemedy,
			[ConnectionFileProblem.HostNotIPv4] = Resources.FailureConnectionFileHostNotIPv4Remedy
		};

	[Fact]
	public void ConnectionFileRemedyTable_CoversEveryMemberOfTheEnum()
	{
		_connectionFileRemedies.Keys.Should().BeEquivalentTo(Enum.GetValues<ConnectionFileProblem>());
	}

	[Fact]
	public void EveryConnectionFileProblem_CarriesItsOwnRemedyRatherThanTheGenericOne()
	{
		foreach (var (problem, remedy) in _connectionFileRemedies)
		{
			var view = ArchiveFailureMapper.Map(
				new ConnectionFileError(@"C:\config\archive", problem, "host"));

			view.Remedy.Should().Be(remedy, "ConnectionFileProblem.{0}", problem);
			view.Remedy.Should().NotBe(Resources.FailureConnectionFileRejectedRemedy);
		}
	}

	// These three switch on an enum and throw on an unknown member, so the table pins the arm count and the
	// call pins that no member reaches the throw.
	[Fact]
	public void EveryEnumMapperArm_AnswersForEveryMemberOfItsEnum()
	{
		foreach (var problem in Enum.GetValues<StartupArgumentsProblem>())
		{
			ArchiveFailureMapper.Map(new StartupArgumentsError(problem, "--config-dir", "a, b"))
				.Remedy.Should().NotBeNullOrWhiteSpace("StartupArgumentsProblem.{0}", problem);
		}

		foreach (var read in Enum.GetValues<StartupRead>())
		{
			ArchiveFailureMapper.Map(new StartupReadTimedOutError(read, TimeSpan.FromSeconds(5)))
				.Detail.Should().NotBeNullOrWhiteSpace("StartupRead.{0}", read);
		}

		foreach (var section in Enum.GetValues<ConfigurationSectionName>())
		{
			ConfigurationSectionFailureMapper.Map(
					new ConfigurationSectionError(section, @"C:\config", SectionProblem.NoFiles))
				.Title.Should().NotBeNullOrWhiteSpace("ConfigurationSectionName.{0}", section);
		}
	}

	[Fact]
	public void ArchiveFaultTable_CoversEveryMemberOfTheEnum()
	{
		_archiveFaults.Keys.Should().BeEquivalentTo(Enum.GetValues<ArchiveFault>());
	}

	[Fact]
	public void SectionProblemTable_CoversEveryMemberOfTheEnum()
	{
		_sectionProblems.Keys.Should().BeEquivalentTo(Enum.GetValues<SectionProblem>());
	}

	[Fact]
	public void AppSettingsProblemTable_CoversEveryMemberOfTheEnum()
	{
		_appSettingsProblems.Keys.Should().BeEquivalentTo(Enum.GetValues<AppSettingsProblem>());
	}

	// The declared error types are what Map switches on, so a new one with no arm falls into MapUnknown
	// without anyone noticing.
	[Fact]
	public void MapArmTable_CoversEveryErrorTypeThisRepositoryDeclares()
	{
		var declared = new[] { typeof(ArchiveError).Assembly, typeof(App).Assembly }
			.SelectMany(assembly => assembly.GetTypes())
			.Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IError).IsAssignableFrom(type));

		declared.Should().OnlyContain(
			type => type == typeof(ArchiveError) || _mapArms.ContainsKey(type),
			"ArchiveError is covered by the fault table and every other error type needs its own arm");
	}

	[Fact]
	public void EveryArchiveFault_CarriesTheSeverityTheTableNames()
	{
		foreach (var (fault, severity) in _archiveFaults)
		{
			ArchiveFailureMapper.Map(Archive(fault, "detail")).Severity
				.Should().Be(severity, "ArchiveFault.{0} carries a detail", fault);
			ArchiveFailureMapper.Map(Archive(fault)).Severity
				.Should().Be(severity, "ArchiveFault.{0} carries no detail", fault);
		}
	}

	[Fact]
	public void EverySectionProblem_CarriesTheSeverityTheTableNames()
	{
		foreach (var (problem, severity) in _sectionProblems)
		{
			ConfigurationSectionFailureMapper.Map(Section(problem)).Severity
				.Should().Be(severity, "SectionProblem.{0}", problem);
		}
	}

	[Fact]
	public void EveryAppSettingsProblem_CarriesTheSeverityTheTableNames()
	{
		foreach (var (problem, severity) in _appSettingsProblems)
		{
			ArchiveFailureMapper.Map(new AppSettingsError(@"C:\config\ui", problem, "locale", "ru, en")).Severity
				.Should().Be(severity, "AppSettingsProblem.{0}", problem);
		}
	}

	[Fact]
	public void EveryMapArm_CarriesTheSeverityTheTableNames()
	{
		foreach (var (type, row) in _mapArms)
		{
			ArchiveFailureMapper.Map(row.Sample).Severity
				.Should().Be(row.Severity, "the {0} arm", type.Name);
		}
	}

	[Fact]
	public void ArchiveFaults_SplitIntoWhatRetriesAndWhatNeedsTheOperator()
	{
		ArchiveFault[] retried =
		[
			ArchiveFault.Unreachable,
			ArchiveFault.ConnectionLost,
			ArchiveFault.QueryTimedOut,
			ArchiveFault.ReadFailed
		];
		ArchiveFault[] actedOn =
		[
			ArchiveFault.AccessDenied,
			ArchiveFault.DatabaseMissing,
			ArchiveFault.TableMissing,
			ArchiveFault.ShapeUnexpected
		];

		retried.Concat(actedOn).Should().BeEquivalentTo(
			Enum.GetValues<ArchiveFault>(),
			"every fault is either retried or acted on, and a new member belongs to one of them");
		retried.Select(Severity).Should().AllBeEquivalentTo(MessageSeverity.Warning);
		actedOn.Select(Severity).Should().AllBeEquivalentTo(MessageSeverity.Error);
	}

	private static MessageSeverity Severity(ArchiveFault fault)
	{
		return ArchiveFailureMapper.Map(Archive(fault, "detail")).Severity;
	}

	private static ArchiveError Archive(ArchiveFault kind, string detail = "")
	{
		return new ArchiveError(kind, "scada-host", 5432, "semiplot", detail);
	}

	private static ConfigurationSectionError Section(SectionProblem problem)
	{
		return new ConfigurationSectionError(
			ConfigurationSectionName.App,
			@"C:\config\ui",
			problem,
			"locale",
			["app.yaml", "locale.yaml"]);
	}
}
