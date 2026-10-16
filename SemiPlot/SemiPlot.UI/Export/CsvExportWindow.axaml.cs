using System.Reactive.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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

	private async void OnSaveAsClick(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (DataContext is not CsvExportViewModel { Form: { From: { } from, To: { } to } } viewModel)
			{
				return;
			}

			var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
			{
				Title = LocalizedText.ExportWindowTitle,
				SuggestedFileName = CsvFileExporter.SuggestedFileName(from, to),
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
