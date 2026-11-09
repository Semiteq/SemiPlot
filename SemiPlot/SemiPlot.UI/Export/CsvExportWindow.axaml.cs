using System.Reactive.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;

using ReactiveUI;

using SemiPlot.UI.Navigation;

using LocalizedText = SemiPlot.UI.Localization.Resources;

namespace SemiPlot.UI.Export;

public partial class CsvExportWindow : Window
{
	private IDisposable? _blackoutSync;

	public CsvExportWindow()
	{
		InitializeComponent();
		ExportStartDate.TemplateApplied += OnPickerTemplateApplied;
		ExportEndDate.TemplateApplied += OnPickerTemplateApplied;
		DataContextChanged += OnDataContextChanged;
		ExportPenItems.AddHandler(KeyDownEvent, OnPenItemsKeyDown, RoutingStrategies.Tunnel);
	}

	protected override void OnClosed(EventArgs e)
	{
		_blackoutSync?.Dispose();
		_blackoutSync = null;

		base.OnClosed(e);
	}

	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_blackoutSync?.Dispose();
		_blackoutSync = null;

		if (DataContext is not CsvExportViewModel viewModel)
		{
			return;
		}

		_blackoutSync = viewModel.WhenAnyValue(model => model.Days.BlackoutDays)
			.Subscribe(ApplyBlackoutDays, viewModel.ReportFailure);
	}

	private void OnPickerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
	{
		if (DataContext is CsvExportViewModel viewModel)
		{
			ApplyBlackoutDays(viewModel.Days.BlackoutDays);
		}
	}

	private void ApplyBlackoutDays(IReadOnlyList<DateTime> days)
	{
		try
		{
			CalendarBlackout.Apply(days, ExportStartDate, ExportEndDate);
		}
		catch (Exception failure)
		{
			(DataContext as CsvExportViewModel)?.ReportFailure(failure);
		}
	}

	/// <summary>
	/// Moves the focus between the boxes on the arrow, page, Home and End keys; ItemsControl's own handler and the
	/// scroller's would scroll the list and leave the focus behind.
	/// </summary>
	private void OnPenItemsKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.KeyModifiers != KeyModifiers.None || e.Source is not Visual source || RowOf(source) is not { } row)
		{
			return;
		}

		var index = ExportPenItems.IndexFromContainer(row);
		var lastIndex = ExportPenItems.ItemCount - 1;
		int? target = e.Key switch
		{
			Key.Up => index - 1,
			Key.Down => index + 1,
			Key.PageUp => Math.Max(index - RowsPerPage(row), 0),
			Key.PageDown => Math.Min(index + RowsPerPage(row), lastIndex),
			Key.Home => 0,
			Key.End => lastIndex,
			_ => null
		};

		if (target is not { } targetIndex)
		{
			return;
		}

		e.Handled = true;
		if (targetIndex < 0 || targetIndex >= ExportPenItems.ItemCount)
		{
			return;
		}

		ExportPenItems.ScrollIntoView(targetIndex);
		ExportPenItems.ContainerFromIndex(targetIndex)?
			.GetVisualDescendants()
			.OfType<CheckBox>()
			.FirstOrDefault()?
			.Focus(NavigationMethod.Directional);
	}

	private Control? RowOf(Visual source)
	{
		if (ExportPenItems.ItemsPanelRoot is not { } panel)
		{
			return null;
		}

		return source.GetSelfAndVisualAncestors()
			.OfType<Control>()
			.FirstOrDefault(control => control.GetVisualParent() == panel);
	}

	private int RowsPerPage(Control row)
	{
		if (row.Bounds.Height <= 0)
		{
			return 1;
		}

		return Math.Max(1, (int)Math.Floor(ExportPensScroller.Viewport.Height / row.Bounds.Height));
	}

	private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (DataContext is not CsvExportViewModel viewModel
				|| viewModel.SuggestedFileName() is not { } suggestedName)
			{
				return;
			}

			var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				Title = LocalizedText.ExportWindowTitle,
				SuggestedFileName = suggestedName,
				DefaultExtension = "csv",
				FileTypeChoices =
				[
					new FilePickerFileType(LocalizedText.ExportFileTypeName) { Patterns = ["*.csv"] }
				]
			});

			if (file is null)
			{
				return;
			}

			await viewModel.ExportCommand.Execute(file.Path.LocalPath).ToTask();
		}
		catch (Exception failure)
		{
			(DataContext as CsvExportViewModel)?.ReportFailure(failure);
		}
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
