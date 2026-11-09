using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using SemiPlot.UI.Startup;
using SemiPlot.UI.Windows;

namespace SemiPlot.UI.Settings;

public partial class SettingsDialog : Window
{
	private readonly OwnedWindowClosing _closing;

	public SettingsDialog()
	{
		InitializeComponent();

		_closing = new OwnedWindowClosing(this, () => (DataContext as SettingsViewModel)?.SaveInFlight);
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

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (_closing.Lets(e))
		{
			base.OnClosing(e);
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		_closing.CloseOnEscape(e);
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
