using Avalonia.Controls;
using Avalonia.Interactivity;

using SemiPlot.UI.Startup;

namespace SemiPlot.UI.Settings;

public partial class SettingsDialog : Window
{
	public SettingsDialog()
	{
		InitializeComponent();
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);
		ActualThemeVariantChanged += OnActualThemeVariantChanged;
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		ActualThemeVariantChanged -= OnActualThemeVariantChanged;
		base.OnUnloaded(e);
	}

	// docs/architecture/overview.md#the-live-theme
	private void OnActualThemeVariantChanged(object? sender, EventArgs e)
	{
		if (DataContext is SettingsViewModel viewModel && SettingsVocabulary.Of(ActualThemeVariant) is { } applied)
		{
			viewModel.FollowAppliedTheme(applied.Theme);
		}
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
