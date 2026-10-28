using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;

using SemiPlot.Core.Data;

using Serilog;

using LocalizedText = SemiPlot.UI.Localization.Resources;

namespace SemiPlot.UI.Chart;

/// <summary>The chart's right-click menus and the marker label editor the rename opens.</summary>
public partial class TrendChartView
{
	private DateTime _menuMoment;
	private StoredMarker? _menuMarker;
	private Func<string?, Task>? _markerLabelCommit;

	private void OpenChartMenu(Point position)
	{
		var dataRect = PlotControl.Plot.LastRender.DataRect;
		if (_viewModel is null || dataRect.Width <= 0f || !dataRect.Contains((float)position.X, (float)position.Y))
		{
			return;
		}

		_menuMoment = AnchorAt(position);
		_menuMarker = MarkerAt(position);
		if (_menuMarker is null)
		{
			_chartMenuFlyout.ShowAt(PlotControl, true);
			return;
		}

		MarkerMenuLock.Header = _menuMarker.IsLocked ? LocalizedText.MarkerMenuUnlock : LocalizedText.MarkerMenuLock;
		_markerMenuFlyout.ShowAt(PlotControl, true);
	}

	private async void OnAddMarkerClick(object? sender, RoutedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel)
		{
			return;
		}

		try
		{
			await viewModel.Markers.AddAtAsync(_menuMoment);
		}
		catch (Exception failure)
		{
			Log.Error(failure, "The chart view failed to add a marker");
			viewModel.ReportFailure(failure);
		}
	}

	private void OnRenameMarkerClick(object? sender, RoutedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel || _menuMarker is not { } marker)
		{
			return;
		}

		OpenMarkerLabelEditor(marker.Label ?? string.Empty, label => viewModel.Markers.RenameAsync(marker, label));
	}

	private async void OnLockMarkerClick(object? sender, RoutedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel || _menuMarker is not { } marker)
		{
			return;
		}

		try
		{
			await viewModel.Markers.ToggleLockAsync(marker);
		}
		catch (Exception failure)
		{
			Log.Error(failure, "The chart view failed to store a marker lock");
			viewModel.ReportFailure(failure);
		}
	}

	private async void OnDeleteMarkerClick(object? sender, RoutedEventArgs eventArgs)
	{
		if (_viewModel is not { } viewModel || _menuMarker is not { } marker)
		{
			return;
		}

		try
		{
			await viewModel.Markers.DeleteAsync(marker);
		}
		catch (Exception failure)
		{
			Log.Error(failure, "The chart view failed to delete a marker");
			viewModel.ReportFailure(failure);
		}
	}

	private void OpenMarkerLabelEditor(string label, Func<string?, Task> commit)
	{
		_markerLabelCommit = commit;
		MarkerLabelBox.Text = label;
		_markerLabelFlyout.ShowAt(PlotControl, true);
	}

	private void OnMarkerLabelFlyoutOpened(object? sender, EventArgs eventArgs)
	{
		MarkerLabelBox.Focus();
		MarkerLabelBox.SelectAll();
	}

	private async void OnMarkerLabelOkClick(object? sender, RoutedEventArgs eventArgs)
	{
		var viewModel = _viewModel;
		try
		{
			await CommitMarkerLabelAsync();
		}
		catch (Exception failure)
		{
			ReportMarkerLabelFailure(viewModel, failure);
		}
	}

	private void OnMarkerLabelCancelClick(object? sender, RoutedEventArgs eventArgs)
	{
		CloseMarkerLabelEditor();
	}

	private async void OnMarkerLabelKeyDown(object? sender, KeyEventArgs eventArgs)
	{
		var viewModel = _viewModel;
		try
		{
			if (eventArgs.Key == Key.Escape)
			{
				eventArgs.Handled = true;
				CloseMarkerLabelEditor();
			}
			else if (eventArgs.Key == Key.Enter)
			{
				eventArgs.Handled = true;
				await CommitMarkerLabelAsync();
			}
		}
		catch (Exception failure)
		{
			ReportMarkerLabelFailure(viewModel, failure);
		}
	}

	private Task CommitMarkerLabelAsync()
	{
		if (_markerLabelCommit is not { } commit)
		{
			return Task.CompletedTask;
		}

		var typedLabel = MarkerLabelBox.Text;
		CloseMarkerLabelEditor();

		return commit(typedLabel);
	}

	private static void ReportMarkerLabelFailure(TrendChartViewModel? viewModel, Exception failure)
	{
		Log.Error(failure, "The chart view failed to store a marker label");
		viewModel?.ReportFailure(failure);
	}

	private void CloseMarkerLabelEditor()
	{
		_markerLabelCommit = null;
		_markerLabelFlyout.Hide();
	}
}
