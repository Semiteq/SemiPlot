using SemiPlot.UI.Export;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;

namespace SemiPlot.UI.MainWindow;

internal sealed class OpenViewModels
{
	public SettingsViewModel? Settings { get; set; }

	public PenEditorViewModel? PenEditor { get; set; }

	public CsvExportViewModel? Export { get; set; }

	public void Forget(object viewModel)
	{
		if (ReferenceEquals(viewModel, Settings))
		{
			Settings = null;
		}
		else if (ReferenceEquals(viewModel, PenEditor))
		{
			PenEditor = null;
		}
		else if (ReferenceEquals(viewModel, Export))
		{
			Export = null;
		}
	}
}
