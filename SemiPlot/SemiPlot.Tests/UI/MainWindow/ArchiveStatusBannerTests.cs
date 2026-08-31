using System.ComponentModel;
using System.Reactive.Subjects;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.MainWindow;

using Xunit;

namespace SemiPlot.Tests.UI.MainWindow;

/// <summary>
/// The archive-connection row of the main window: written by the poll's connection stream alone, and
/// withdrawn by it on the next successful tick.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatusBannerTests
{
	private static readonly ArchiveConnectionState _lost =
		new(new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3"));

	[AvaloniaFact]
	public void ArchiveConnectionMessage_BeforeAnyState_IsAbsent()
	{
		using var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);

		viewModel.ArchiveConnectionMessage.Should().BeNull();
		viewModel.HasArchiveConnectionMessage.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ArchiveConnectionMessage_WithoutAnyBinding_IsAbsent()
	{
		using var viewModel = new MainWindowViewModel();

		viewModel.ArchiveConnectionMessage.Should().BeNull();
		viewModel.HasArchiveConnectionMessage.Should().BeFalse();
	}

	// The row is the whole of what the operator is told about a lost live edge, so it carries the remedy
	// and not only the state: StartupFailureMapper holds one per error type, and rendering IError.Message
	// would leave the row naming a fault with no action beside it.
	[AvaloniaFact]
	public void ArchiveConnectionMessage_OnAFault_CarriesTheStateAndItsRemedy()
	{
		using var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);
		var observed = Observe(viewModel, nameof(MainWindowViewModel.HasArchiveConnectionMessage),
			() => viewModel.HasArchiveConnectionMessage);

		states.OnNext(_lost);

		viewModel.ArchiveConnectionMessage.Should()
			.Contain("semiplot_dev").And.Contain("bench:5432")
			.And.Contain("stopped answering after 3 consecutive failed reads")
			.And.Contain("history already drawn is unaffected")
			.And.Contain("SemiPlot keeps polling and clears this by itself once the archive answers again.");
		viewModel.ArchiveConnectionMessage.Should().NotBe(
			_lost.Fault!.Message, "the raw error sentence names a state and no action");
		viewModel.HasArchiveConnectionMessage.Should().BeTrue();
		observed.Should().Equal(true);
	}

	[AvaloniaFact]
	public void ArchiveConnectionMessage_OnTheFirstSuccessAfterAFault_IsWithdrawn()
	{
		using var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);
		states.OnNext(_lost);
		var observed = Observe(viewModel, nameof(MainWindowViewModel.ArchiveConnectionMessage),
			() => viewModel.ArchiveConnectionMessage);

		states.OnNext(ArchiveConnectionState.Connected);

		viewModel.ArchiveConnectionMessage.Should().BeNull();
		viewModel.HasArchiveConnectionMessage.Should().BeFalse();
		observed.Should().ContainSingle().Which.Should().BeNull();
	}

	[AvaloniaFact]
	public void ArchiveConnectionMessage_AfterDisposal_StopsFollowingTheStream()
	{
		var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);
		viewModel.Dispose();

		states.OnNext(_lost);

		viewModel.ArchiveConnectionMessage.Should().BeNull();
	}

	[AvaloniaFact]
	public void ObserveArchiveConnection_ASecondTime_IsRefused()
	{
		using var viewModel = new MainWindowViewModel();
		using var first = new Subject<ArchiveConnectionState>();
		using var second = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(first);

		var bindAgain = () => viewModel.ObserveArchiveConnection(second);

		bindAgain.Should().Throw<InvalidOperationException>();
	}

	// Records the value each notification carries, so a test reads both how often a row changed and what
	// it changed to.
	private static List<T> Observe<T>(MainWindowViewModel viewModel, string propertyName, Func<T> read)
	{
		var observed = new List<T>();
		((INotifyPropertyChanged)viewModel).PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == propertyName)
			{
				observed.Add(read());
			}
		};

		return observed;
	}
}
