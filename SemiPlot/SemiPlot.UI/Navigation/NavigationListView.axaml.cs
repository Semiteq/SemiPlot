using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using ReactiveUI;

namespace SemiPlot.UI.Navigation;

/// <summary>
/// A navigation list's flyout content: focus, scrolling and keys; the view model decides.
/// </summary>
public partial class NavigationListView : UserControl
{
	public const double ListWidth = 400.0;

	/// <summary>An entry row's height, which the row template reads.</summary>
	public const double RowHeight = 28.0;

	/// <summary>A month header's height, which the header template reads.</summary>
	public const double HeaderHeight = 24.0;

	/// <summary>The scroller's fixed height: ten entry rows, so neither a filter nor a read resizes the list.</summary>
	public const double ViewportHeight = VisibleRows * RowHeight;

	/// <summary>A run row's flag slot, which holds the interrupted glyph.</summary>
	public const double FlagColumnWidth = 16.0;

	/// <summary>The date column, which holds the widest <c>d MMM HH:mm</c> in either language.</summary>
	public const double DateColumnWidth = 108.0;

	/// <summary>A run row's duration column, which holds <c>99 h 59 min</c> in either language.</summary>
	public const double DurationColumnWidth = 88.0;

	private const int VisibleRows = 10;

	private IDisposable? _rowsFollow;

	public NavigationListView()
	{
		InitializeComponent();

		AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
		DataContextChanged += OnDataContextChanged;
	}

	/// <summary>Raised when a key or a click has finished with the list, so its flyout closes.</summary>
	public event EventHandler? DismissRequested;

	/// <summary>Clears the filter and activates the nearest entry, ahead of the opening's first layout pass.</summary>
	public void Prepare()
	{
		if (DataContext is NavigationListViewModel viewModel)
		{
			viewModel.Open();
		}
	}

	/// <summary>Focuses the filter and scrolls to the active row once the shown list has its layout.</summary>
	public void FocusFilter()
	{
		ListFilter.Focus();
		Dispatcher.UIThread.Post(ScrollToActive, DispatcherPriority.Background);
	}

	/// <summary>Scrolls to the active row after every rebuild of the rows: a filter typed, an entry read.</summary>
	private void OnDataContextChanged(object? sender, EventArgs e)
	{
		_rowsFollow?.Dispose();
		_rowsFollow = (DataContext as NavigationListViewModel)?
			.WhenAnyValue(list => list.Rows)
			.Subscribe(_ => Dispatcher.UIThread.Post(ScrollToActive, DispatcherPriority.Background));
	}

	private void OnKeyDown(object? sender, KeyEventArgs args)
	{
		if (DataContext is not NavigationListViewModel viewModel)
		{
			return;
		}

		switch (args.Key)
		{
			case Key.Down or Key.Up:
				viewModel.MoveActive(args.Key == Key.Down ? 1 : -1);
				ScrollToActive();
				args.Handled = true;
				break;
			case Key.Enter:
				args.Handled = true;
				if (viewModel.GoToActive())
				{
					DismissRequested?.Invoke(this, EventArgs.Empty);
				}

				break;
			case Key.Escape:
				args.Handled = true;
				DismissRequested?.Invoke(this, EventArgs.Empty);
				break;
		}
	}

	private void OnRowTapped(object? sender, TappedEventArgs args)
	{
		if (DataContext is NavigationListViewModel viewModel
			&& sender is Control { DataContext: NavigationListItem item })
		{
			viewModel.GoTo(item);
			DismissRequested?.Invoke(this, EventArgs.Empty);
		}
	}

	private void ScrollToActive()
	{
		if (DataContext is NavigationListViewModel { ActiveIndex: >= 0 and var index })
		{
			ListItems.ScrollIntoView(index);
		}
	}
}
