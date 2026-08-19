using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SemiPlot.UI.Startup;

/// <summary>
/// The only window a failed startup opens. It resolves no service and reads no configuration — it is
/// shown exactly when the container or the archive could not be brought up — so everything it needs
/// arrives as a <see cref="StartupFailureView"/> in its data context.
/// </summary>
public partial class ErrorWindow : Window
{
	public ErrorWindow()
	{
		InitializeComponent();
	}

	public ErrorWindow(StartupFailureView failure) : this()
	{
		ArgumentNullException.ThrowIfNull(failure);

		DataContext = failure;
	}

	private void InitializeComponent()
	{
		AvaloniaXamlLoader.Load(this);
	}

	private void OnCloseClick(object? sender, RoutedEventArgs e)
	{
		Close();
	}
}
