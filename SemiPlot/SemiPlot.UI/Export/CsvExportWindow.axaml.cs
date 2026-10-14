using System.Reactive.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using LocalizedText = SemiPlot.UI.Localization.Resources;

namespace SemiPlot.UI.Export;

public partial class CsvExportWindow : Window
{
	public CsvExportWindow()
	{
		InitializeComponent();
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
