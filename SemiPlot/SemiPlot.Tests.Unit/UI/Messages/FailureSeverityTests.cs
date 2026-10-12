using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI;
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
			[ArchiveFault.ValueRejected] = MessageSeverity.Warning,
			[ArchiveFault.NameTaken] = MessageSeverity.Warning,
			[ArchiveFault.RowGone] = MessageSeverity.Warning,
			[ArchiveFault.AccessDenied] = MessageSeverity.Error,
			[ArchiveFault.DatabaseMissing] = MessageSeverity.Error,
			[ArchiveFault.TableMissing] = MessageSeverity.Error,
			[ArchiveFault.ShapeUnexpected] = MessageSeverity.Error
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
				new ConnectionFileError(@"C:\config\archive", ConnectionFileProblem.MissingField, ["host"]),
				MessageSeverity.Error),
			[typeof(StartupReadTimedOutError)] = (
				new StartupReadTimedOutError(StartupRead.PenCatalogue, TimeSpan.FromSeconds(5)),
				MessageSeverity.Error),
			[typeof(InstanceHostUnknownError)] = (new InstanceHostUnknownError(), MessageSeverity.Error),
			[typeof(ExceptionalError)] = (
				new ExceptionalError(new InvalidOperationException("boom")),
				MessageSeverity.Error),
			[typeof(Error)] = (
				new Error("something this build never named"),
				MessageSeverity.Error)
		};

	// These switch on an enum and throw on an unknown member, so mapping every member of each enum pins
	// that no member reaches the throw.
	[Fact]
	public void EveryEnumMapperArm_AnswersForEveryMemberOfItsEnum()
	{
		foreach (var problem in Enum.GetValues<StartupArgumentsProblem>())
		{
			ArchiveFailureMapper.Map(new StartupArgumentsError(problem, "--config-dir", "a, b"))
				.Remedy.Should().NotBeNullOrWhiteSpace("StartupArgumentsProblem.{0}", problem);
		}

		foreach (var problem in Enum.GetValues<ConnectionFileProblem>())
		{
			ArchiveFailureMapper.Map(new ConnectionFileError(@"C:\config\archive", problem, ["host"]))
				.Remedy.Should().NotBeNullOrWhiteSpace("ConnectionFileProblem.{0}", problem);
		}

		foreach (var read in Enum.GetValues<StartupRead>())
		{
			ArchiveFailureMapper.Map(new StartupReadTimedOutError(read, TimeSpan.FromSeconds(5)))
				.Detail.Should().NotBeNullOrWhiteSpace("StartupRead.{0}", read);
		}

		foreach (var problem in Enum.GetValues<SectionProblem>())
		{
			ConfigurationSectionFailureMapper.Map(Section(problem))
				.Remedy.Should().NotBeNullOrWhiteSpace("SectionProblem.{0}", problem);
		}

		foreach (var problem in Enum.GetValues<AppSettingsProblem>())
		{
			ArchiveFailureMapper.Map(new AppSettingsError(@"C:\config\ui", problem, "locale", "ru, en"))
				.Remedy.Should().NotBeNullOrWhiteSpace("AppSettingsProblem.{0}", problem);
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
	public void EveryMapArm_CarriesTheSeverityTheTableNames()
	{
		foreach (var (type, row) in _mapArms)
		{
			ArchiveFailureMapper.Map(row.Sample).Severity
				.Should().Be(row.Severity, "the {0} arm", type.Name);
		}
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
