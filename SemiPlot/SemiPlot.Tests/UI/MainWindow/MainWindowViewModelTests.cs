using System.ComponentModel;
using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;

using Xunit;

namespace SemiPlot.Tests.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	[Fact]
	public void PenCount_WithoutChart_IsZero()
	{
		using var viewModel = new MainWindowViewModel();

		viewModel.PenCount.Should().Be(0);
	}

	[AvaloniaFact]
	public void ChartViewModel_WhenAssigned_PublishesThePenCount()
	{
		using var viewModel = new MainWindowViewModel();
		var (chart, expectedPenCount) = CreateChartWithPens();
		var observedPenCounts = ObservePenCount(viewModel);

		viewModel.ChartViewModel = chart;

		viewModel.PenCount.Should().Be(expectedPenCount);
		observedPenCounts.Should().Equal(expectedPenCount);
	}

	[AvaloniaFact]
	public void ChartViewModel_WhenReassigned_PublishesTheNewPenCount()
	{
		using var viewModel = new MainWindowViewModel();
		var (first, firstPenCount) = CreateChartWithPens();
		var (second, secondPenCount) = CreateChartWithPens(1);
		secondPenCount.Should().NotBe(firstPenCount);
		viewModel.ChartViewModel = first;
		var observedPenCounts = ObservePenCount(viewModel);

		viewModel.ChartViewModel = second;

		viewModel.PenCount.Should().Be(secondPenCount);
		observedPenCounts.Should().Equal(secondPenCount);
	}

	[AvaloniaFact]
	public void ChartViewModel_WhenClearedToNull_PublishesAZeroPenCount()
	{
		using var viewModel = new MainWindowViewModel();
		var (chart, _) = CreateChartWithPens();
		viewModel.ChartViewModel = chart;
		var observedPenCounts = ObservePenCount(viewModel);

		viewModel.ChartViewModel = null;

		viewModel.PenCount.Should().Be(0);
		observedPenCounts.Should().Equal(0);
	}

	[AvaloniaFact]
	public void ChartViewModel_WhenAssignedTheSameInstance_KeepsTheChartAlive()
	{
		using var viewModel = new MainWindowViewModel();
		var (chart, _) = CreateChartWithPens();
		viewModel.ChartViewModel = chart;
		var activePenId = chart.ActivePenId;
		var observedPenCounts = ObservePenCount(viewModel);

		viewModel.ChartViewModel = chart;

		// Every mutating member throws ObjectDisposedException once the chart is disposed.
		chart.SetActivePen(activePenId).Should().BeTrue();
		observedPenCounts.Should().BeEmpty();
	}

	// The empty-catalogue sentence is bound, so it appears only if the property publishes on the same
	// assignment PenCount publishes on.
	[AvaloniaFact]
	public void ChartViewModel_WhenAssignedWithNoPens_PublishesTheEmptyCatalogueState()
	{
		using var viewModel = new MainWindowViewModel();
		viewModel.IsCatalogueEmpty.Should().BeFalse();
		var (chart, penCount) = CreateChartWithPens(0);
		penCount.Should().Be(0);
		var observed = ObserveEmptyCatalogueState(viewModel);

		viewModel.ChartViewModel = chart;

		viewModel.IsCatalogueEmpty.Should().BeTrue();
		observed.Should().Equal(true);
	}

	private static List<bool> ObserveEmptyCatalogueState(MainWindowViewModel viewModel)
	{
		var observed = new List<bool>();
		((INotifyPropertyChanged)viewModel).PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(MainWindowViewModel.IsCatalogueEmpty))
			{
				observed.Add(viewModel.IsCatalogueEmpty);
			}
		};

		return observed;
	}

	private static List<int> ObservePenCount(MainWindowViewModel viewModel)
	{
		var observed = new List<int>();
		((INotifyPropertyChanged)viewModel).PropertyChanged += (_, args) =>
		{
			if (args.PropertyName == nameof(MainWindowViewModel.PenCount))
			{
				observed.Add(viewModel.PenCount);
			}
		};

		return observed;
	}

	private static (TrendChartViewModel Chart, int PenCount) CreateChartWithPens(int? penLimit = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var chart = new TrendChartViewModel(
			coordinator, scheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);
		var pens = provider.Pens.Take(penLimit ?? provider.Pens.Count).ToArray();

		foreach (var pen in pens)
		{
			chart.AddPen(pen);
		}

		return (chart, pens.Length);
	}
}
