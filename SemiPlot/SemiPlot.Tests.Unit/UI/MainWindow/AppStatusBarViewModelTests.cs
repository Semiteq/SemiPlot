using System.Globalization;
using System.Reactive.Subjects;
using System.Resources;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Messages;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>
/// The status bar's own state: the connection it follows, the layer named from resx, and the two entries
/// the connection writes into the message panel.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class AppStatusBarViewModelTests
{
	private static readonly ArchiveConnectionState _lost =
		new(new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3"));

	[Fact]
	public void Connected_AsTheFirstTick_WritesNoEntry()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);

		states.OnNext(ArchiveConnectionState.Connected);

		panel.Entries.Should().BeEmpty();
		statusBar.IsConnected.Should().BeTrue();
		statusBar.ConnectionText.Should().Be(Resources.StatusConnectionOk);
	}

	[Fact]
	public void Connected_AfterAFault_WritesExactlyOneInfoEntry()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);

		states.OnNext(ArchiveConnectionState.Connected);
		states.OnNext(_lost);
		states.OnNext(ArchiveConnectionState.Connected);

		panel.Entries.Should().HaveCount(2);
		panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Info);
		panel.Entries[0].View.Title.Should().Be(Resources.StatusConnectionRestoredTitle);
		panel.Entries[1].View.Should().Be(ArchiveFailureMapper.Map(_lost.Fault!));
		statusBar.IsConnected.Should().BeTrue();
	}

	[Fact]
	public void Connected_TwiceAfterOneFault_WritesOneRecoveryEntry()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);

		states.OnNext(_lost);
		states.OnNext(ArchiveConnectionState.Connected);
		states.OnNext(ArchiveConnectionState.Connected);

		panel.Entries.Where(entry => entry.View.Severity == MessageSeverity.Info).Should().ContainSingle();
		panel.Entries[0].RepeatCount.Should().Be(1, "a second Connected owes no second entry");
	}

	[Fact]
	public void AFault_SetsTheFaultStateAndItsText()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);

		states.OnNext(_lost);

		statusBar.IsConnected.Should().BeFalse();
		statusBar.ConnectionText.Should().Be(Resources.StatusConnectionFault);
		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Warning);
	}

	// TrendCoordinator forwards this stream with a bare Subscribe, so an escaping throw would end the
	// forwarding subscription and the bar would never move again.
	[Fact]
	public void AThrowInTheHandler_IsReportedAndTheStreamSurvives()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new ThrowingLogger<AppStatusBarViewModel>(throwsOnce: true);
		using var statusBar = new AppStatusBarViewModel(panel, logger);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);

		states.OnNext(_lost);

		panel.Entries.Should().HaveCount(2);
		panel.Entries[0].View.Detail.Should().Contain(nameof(InvalidOperationException));
		panel.Entries[0].View.Detail.Should().Contain("the log sink threw");
		panel.Entries[1].View.Severity.Should().Be(MessageSeverity.Warning);

		states.OnNext(ArchiveConnectionState.Connected);

		statusBar.IsConnected.Should().BeTrue("the next state still arrives");
		panel.Entries.Should().HaveCount(3);
		panel.Entries[0].View.Severity.Should().Be(MessageSeverity.Info);
	}

	// The panel edit is the likeliest thrower of the two, and it is also what the catch reports through, so
	// a second throw out of it would escape into the coordinator's bare forwarding subscription.
	[Fact]
	public void AThrowOutOfThePanel_IsLoggedAndTheStreamSurvives()
	{
		using var panel = new MessagePanelViewModel();
		var logger = new RecordingLogger<AppStatusBarViewModel>();
		using var statusBar = new AppStatusBarViewModel(panel, logger);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);
		var poison = ReportingTestDoubles.PoisonEntries(panel);

		states.OnNext(_lost);

		logger.Failures.Should().HaveCount(2, "the failure being reported is logged before the refusal is");
		logger.Failures.Should().OnlyContain(entry => entry.Message == "the bound list threw");

		poison.Dispose();
		states.OnNext(ArchiveConnectionState.Connected);

		statusBar.IsConnected.Should().BeTrue("the next state still arrives");
		panel.Entries[0].View.Severity.Should().Be(
			MessageSeverity.Info, "the fault flag survived the failed report");
	}

	[Fact]
	public void TrackArchiveConnection_ASecondTime_IsRefused()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		using var first = new Subject<ArchiveConnectionState>();
		using var second = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(first);

		var bindAgain = () => statusBar.TrackArchiveConnection(second);

		bindAgain.Should().Throw<InvalidOperationException>();
	}

	[Fact]
	public void AfterDisposal_TheBarStopsFollowingTheStream()
	{
		using var panel = new MessagePanelViewModel();
		var statusBar = NewStatusBar(panel);
		using var states = new Subject<ArchiveConnectionState>();
		statusBar.TrackArchiveConnection(states);
		statusBar.Dispose();

		states.OnNext(_lost);

		statusBar.IsConnected.Should().BeTrue();
		panel.Entries.Should().BeEmpty();
	}

	[Theory]
	[InlineData(AggregationLayer.Raw, "StatusLayerRaw")]
	[InlineData(AggregationLayer.Minute, "StatusLayerMinute")]
	[InlineData(AggregationLayer.Hour, "StatusLayerHour")]
	[InlineData(AggregationLayer.Day, "StatusLayerDay")]
	public void LayerText_ForEachMember_IsTheRussianResourceValue(AggregationLayer layer, string key)
	{
		var previous = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = new CultureInfo("ru");
			var expectedName = RussianSet().GetString(key);
			expectedName.Should().NotBeNullOrWhiteSpace();
			expectedName.Should().NotBe(layer.ToString());

			using var panel = new MessagePanelViewModel();
			using var statusBar = NewStatusBar(panel);
			var navigation = NavigationAtRawLayer();
			statusBar.TrackLayer(navigation);
			DriveToLayer(navigation, layer);

			statusBar.ActiveLayer.Should().Be(layer);
			statusBar.LayerText.Should().Be(Resources.FormatStatusLayerFormat(expectedName!));
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}

	[Fact]
	public void TrackLayer_WithNull_StopsFollowingThePreviousChart()
	{
		using var panel = new MessagePanelViewModel();
		using var statusBar = NewStatusBar(panel);
		var navigation = NavigationAtRawLayer();
		statusBar.TrackLayer(navigation);
		statusBar.TrackLayer(null);

		DriveToLayer(navigation, AggregationLayer.Day);

		statusBar.ActiveLayer.Should().Be(AggregationLayer.Raw);
	}

	private static ResourceSet RussianSet()
	{
		var set = Resources.ResourceManager.GetResourceSet(
			new CultureInfo("ru"), createIfNotExists: true, tryParents: false);
		set.Should().NotBeNull();

		return set;
	}
}
