using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SemiPlot.UI.Minimap;

/// <summary>
/// A minimap track's left press, drag and hover (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
internal sealed class TrackPointer
{
	private readonly Control _track;
	private readonly Action<double> _navigate;
	private readonly Action<double?> _hover;
	private bool _isDragging;

	public TrackPointer(Control track, Action<double> navigate, Action<double?> hover)
	{
		_track = track;
		_navigate = navigate;
		_hover = hover;

		track.PointerPressed += OnPressed;
		track.PointerMoved += OnMoved;
		track.PointerReleased += OnReleased;
		track.PointerCaptureLost += OnCaptureLost;
		track.PointerExited += OnExited;
	}

	private void OnPressed(object? sender, PointerPressedEventArgs args)
	{
		if (!args.GetCurrentPoint(_track).Properties.IsLeftButtonPressed)
		{
			return;
		}

		_isDragging = true;
		args.Pointer.Capture(_track);
		_navigate(args.GetPosition(_track).X);
		args.Handled = true;
	}

	private void OnMoved(object? sender, PointerEventArgs args)
	{
		var position = args.GetPosition(_track);
		if (_isDragging)
		{
			_navigate(position.X);
		}

		_hover(new Rect(_track.Bounds.Size).Contains(position) ? position.X : null);
	}

	private void OnReleased(object? sender, PointerReleasedEventArgs args)
	{
		if (!_isDragging)
		{
			return;
		}

		args.Pointer.Capture(null);
		_isDragging = false;
	}

	// Capture can be lost mid-drag without a PointerReleased (window deactivation); clear the drag flag.
	private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs args)
	{
		_isDragging = false;
	}

	// Capturing the pointer raises PointerExited while it is still over the track, so a drag keeps its hover.
	private void OnExited(object? sender, PointerEventArgs args)
	{
		if (!_isDragging)
		{
			_hover(null);
		}
	}
}
