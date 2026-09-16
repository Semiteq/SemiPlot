using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Messages;

// The entry's text is asserted against the mapper's own output rather than against a literal, so a
// reworded resx value moves both sides and only a seam that stops calling the mapper turns these red.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class ResultReportingTests
{
	[Fact]
	public void ReportFailure_TakesTitleDetailRemedyAndSeverityFromTheMapper()
	{
		using var panel = new MessagePanelViewModel();
		var error = new ArchiveError(ArchiveFault.AccessDenied, "scada-host", 5432, "semiplot", "scada_reader");

		panel.ReportFailure(error, NullLogger.Instance);

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Should().Be(ArchiveFailureMapper.Map(error));
		panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Error);
		panel.Entries[0].View.Title.Should().Be(Resources.FailureArchiveAccessDeniedTitle);
	}

	[Fact]
	public void ReportFailure_OverAResultUsesTheFirstErrorOnly()
	{
		using var panel = new MessagePanelViewModel();
		var first = new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot");
		var second = new ArchiveError(ArchiveFault.TableMissing, "scada-host", 5432, "semiplot", "trends");
		var result = Result.Fail(first).WithError(second);

		panel.ReportFailure(result, NullLogger.Instance);

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Should().Be(ArchiveFailureMapper.Map(first));
	}

	[Fact]
	public void ReportFailure_OverASuccessfulResultWritesNothing()
	{
		using var panel = new MessagePanelViewModel();

		panel.ReportFailure(Result.Ok(), NullLogger.Instance);

		panel.Entries.Should().BeEmpty();
	}

	[Fact]
	public void ReportFailure_WritesOneLogLineAtTheSeveritysLevel()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new RecordingLogger();

		panel.ReportFailure(
			new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot"),
			logger);
		panel.ReportFailure(
			new ArchiveError(ArchiveFault.TableMissing, "scada-host", 5432, "semiplot", "trends"),
			logger);

		logger.Levels.Should().Equal(LogLevel.Warning, LogLevel.Error);
	}

	[Fact]
	public void ReportFailure_DropsARepeatOfTheNewestEntryToDebug()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new RecordingLogger();
		var unreachable = new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot");

		panel.ReportFailure(unreachable, logger);
		panel.ReportFailure(unreachable, logger);
		panel.ReportFailure(unreachable, logger);
		panel.ReportFailure(
			new ArchiveError(ArchiveFault.TableMissing, "scada-host", 5432, "semiplot", "trends"),
			logger);

		panel.Entries.Should().HaveCount(2);
		logger.Levels.Should().Equal(
			LogLevel.Warning, LogLevel.Debug, LogLevel.Debug, LogLevel.Error);
	}

	// The log and the panel are independent sinks.
	[Fact]
	public void ReportFailure_WithALogSinkThatThrows_StillShowsTheEntry()
	{
		using var panel = new MessagePanelViewModel();
		var error = new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot");

		var report = () => panel.ReportFailure(error, new ThrowingLogger());

		report.Should().Throw<InvalidOperationException>();
		panel.Entries.Should().ContainSingle().Which.View.Should().Be(ArchiveFailureMapper.Map(error));
	}

	// The caller is an Rx onError or a dispatcher job, so the guard swallows what the plain overload throws.
	[Fact]
	public void TryReportFailure_WithAPanelThatThrows_KeepsTheFailureInTheLog()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new RecordingLogger();
		ReportingTestDoubles.PoisonEntries(panel);

		var report = () => panel.TryReportFailure(
			new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot"),
			logger);

		report.Should().NotThrow();
		logger.Levels.Should().Equal([LogLevel.Warning, LogLevel.Error]);
	}
}
