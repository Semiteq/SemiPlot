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
/// The two archive-status rows of the main window, and the property that makes them two rows rather
/// than one string. A lost connection and a startup health warning are independent facts with
/// independent lifetimes: the poll withdraws its own row on the next successful tick, while the
/// warning stands until the operator fixes what it names. One shared message would let either writer
/// erase the other's sentence, so each row has exactly one writer and this class pins that.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatusBannerTests
{
	private static readonly ArchiveConnectionState _lost =
		new(new ArchiveConnectionLostError("bench", 5432, "semiplot_dev", 3));

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

	[AvaloniaFact]
	public void ArchiveHealthMessage_WhenSet_RaisesItsOwnRowOnly()
	{
		using var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);
		var observed = Observe(viewModel, nameof(MainWindowViewModel.HasArchiveHealthMessage),
			() => viewModel.HasArchiveHealthMessage);

		viewModel.ArchiveHealthMessage = "The default partition holds rows.";

		viewModel.HasArchiveHealthMessage.Should().BeTrue();
		observed.Should().Equal(true);
		viewModel.HasArchiveConnectionMessage.Should().BeFalse();
	}

	[AvaloniaFact]
	public void ArchiveHealthMessage_SetToTheSameText_PublishesNothing()
	{
		using var viewModel = new MainWindowViewModel();
		viewModel.ArchiveHealthMessage = "The default partition holds rows.";
		var observed = Observe(viewModel, nameof(MainWindowViewModel.HasArchiveHealthMessage),
			() => viewModel.HasArchiveHealthMessage);

		viewModel.ArchiveHealthMessage = "The default partition holds rows.";

		observed.Should().BeEmpty();
	}

	// The point of the split: a connection fault arriving while a health warning stands leaves the
	// warning on screen, and the connection's own withdrawal does not take the warning with it.
	[AvaloniaFact]
	public void TheTwoRows_AreIndependent_AndNeitherWriterClearsTheOther()
	{
		using var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);
		viewModel.ArchiveHealthMessage = "The default partition holds rows.";

		states.OnNext(_lost);

		viewModel.HasArchiveHealthMessage.Should().BeTrue();
		viewModel.HasArchiveConnectionMessage.Should().BeTrue();

		states.OnNext(ArchiveConnectionState.Connected);

		viewModel.HasArchiveConnectionMessage.Should().BeFalse();
		viewModel.ArchiveHealthMessage.Should().Be("The default partition holds rows.");
		viewModel.HasArchiveHealthMessage.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheHealthRow_DoesNotFollowTheConnectionStream()
	{
		using var viewModel = new MainWindowViewModel();
		using var states = new Subject<ArchiveConnectionState>();
		viewModel.ObserveArchiveConnection(states);
		var observed = Observe(viewModel, nameof(MainWindowViewModel.ArchiveHealthMessage),
			() => viewModel.ArchiveHealthMessage);

		states.OnNext(_lost);
		states.OnNext(ArchiveConnectionState.Connected);

		viewModel.ArchiveHealthMessage.Should().BeNull();
		observed.Should().BeEmpty();
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
