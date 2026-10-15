using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

using ReactiveUI;

namespace SemiPlot.UI.Chart;

public partial class TimeWindowPanel : UserControl
{
	private IDisposable? _blackoutSync;

	public TimeWindowPanel()
	{
		InitializeComponent();
		TimeWindowStartDate.TemplateApplied += OnPickerTemplateApplied;
		TimeWindowEndDate.TemplateApplied += OnPickerTemplateApplied;
		DataContextChanged += OnDataContextChanged;
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		Run(viewModel => viewModel.Opened());
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		Run(viewModel => viewModel.Close());

		base.OnUnloaded(e);
	}

	// docs/architecture/trend-interaction.md#the-time-window-panel
	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_blackoutSync?.Dispose();
		_blackoutSync = null;

		if (DataContext is not TimeWindowPanelViewModel viewModel)
		{
			return;
		}

		_blackoutSync = viewModel.WhenAnyValue(panel => panel.BlackoutDays)
			.Subscribe(ApplyBlackoutDays, viewModel.ReportFailure);
	}

	private void OnPickerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
	{
		if (DataContext is TimeWindowPanelViewModel viewModel)
		{
			ApplyBlackoutDays(viewModel.BlackoutDays);
		}
	}

	private void OnPickerKeyUp(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
		{
			Run(viewModel => viewModel.Commit());
		}
	}

	private void ApplyBlackoutDays(IReadOnlyList<DateTime> days)
	{
		Run(_ =>
		{
			foreach (var blackoutDates in new[] { TimeWindowStartDate.BlackoutDates, TimeWindowEndDate.BlackoutDates })
			{
				if (blackoutDates is null)
				{
					continue;
				}

				blackoutDates.Clear();
				foreach (var day in days)
				{
					blackoutDates.Add(new CalendarDateRange(day));
				}
			}
		});
	}

	private void Run(Action<TimeWindowPanelViewModel> step)
	{
		if (DataContext is not TimeWindowPanelViewModel viewModel)
		{
			return;
		}

		try
		{
			step(viewModel);
		}
		catch (Exception exception)
		{
			viewModel.ReportFailure(exception);
		}
	}
}
