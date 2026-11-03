using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace SemiPlot.UI.Tables;

/// <summary>Opens every column no narrower than its header and fits the fill column to the spare width.</summary>
public static class HeaderMeasuredColumns
{
	private const double FitTolerance = 0.5;

	public static readonly AttachedProperty<bool> IsEnabledProperty =
		AvaloniaProperty.RegisterAttached<DataGrid, bool>("IsEnabled", typeof(HeaderMeasuredColumns));

	public static readonly AttachedProperty<bool> IsFillProperty =
		AvaloniaProperty.RegisterAttached<DataGridColumn, bool>("IsFill", typeof(HeaderMeasuredColumns));

	private static readonly AttachedProperty<Fit?> _fitProperty =
		AvaloniaProperty.RegisterAttached<DataGrid, Fit?>("Fit", typeof(HeaderMeasuredColumns));

	static HeaderMeasuredColumns()
	{
		IsEnabledProperty.Changed.AddClassHandler<DataGrid>(OnIsEnabledChanged);
	}

	public static bool GetIsEnabled(DataGrid grid)
	{
		return grid.GetValue(IsEnabledProperty);
	}

	public static void SetIsEnabled(DataGrid grid, bool isEnabled)
	{
		grid.SetValue(IsEnabledProperty, isEnabled);
	}

	public static bool GetIsFill(DataGridColumn column)
	{
		return column.GetValue(IsFillProperty);
	}

	public static void SetIsFill(DataGridColumn column, bool isFill)
	{
		column.SetValue(IsFillProperty, isFill);
	}

	private static void OnIsEnabledChanged(DataGrid grid, AvaloniaPropertyChangedEventArgs change)
	{
		grid.AttachedToVisualTree -= OnAttachedToVisualTree;
		grid.DetachedFromVisualTree -= OnDetachedFromVisualTree;
		grid.LayoutUpdated -= OnLayoutUpdated;
		grid.ClearValue(_fitProperty);

		if (!change.GetNewValue<bool>())
		{
			return;
		}

		grid.SetValue(_fitProperty, new Fit());
		grid.AttachedToVisualTree += OnAttachedToVisualTree;
		grid.DetachedFromVisualTree += OnDetachedFromVisualTree;

		if (TopLevel.GetTopLevel(grid) is not null)
		{
			grid.LayoutUpdated += OnLayoutUpdated;
		}
	}

	private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
	{
		if (sender is DataGrid grid)
		{
			grid.LayoutUpdated += OnLayoutUpdated;
		}
	}

	private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
	{
		if (sender is DataGrid grid)
		{
			grid.LayoutUpdated -= OnLayoutUpdated;
		}
	}

	private static void OnLayoutUpdated(object? sender, EventArgs e)
	{
		if (sender is not DataGrid grid || grid.GetValue(_fitProperty) is not { } fit)
		{
			return;
		}

		if (!fit.HeadersMeasured)
		{
			fit.HeadersMeasured = TryMeasureHeaders(grid);
		}

		if (fit.HeadersMeasured)
		{
			FitFillColumn(grid, fit);
		}
	}

	private static bool TryMeasureHeaders(DataGrid grid)
	{
		var headers = new Dictionary<DataGridColumn, DataGridColumnHeader>();
		foreach (var header in grid.GetVisualDescendants().OfType<DataGridColumnHeader>())
		{
			if (DataGridColumn.GetColumnContainingElement(header) is { } column && grid.Columns.Contains(column))
			{
				headers[column] = header;
			}
		}

		var visibleColumns = grid.Columns.Where(column => column.IsVisible).ToList();
		if (!visibleColumns.All(headers.ContainsKey))
		{
			return false;
		}

		foreach (var column in visibleColumns)
		{
			var header = headers[column];
			header.Measure(Size.Infinity);
			var headerWidth = Math.Ceiling(header.DesiredSize.Width);
			header.InvalidateMeasure();

			column.MinWidth = headerWidth;
			if (column.Width.IsAbsolute && column.Width.Value < headerWidth)
			{
				column.Width = new DataGridLength(headerWidth);
			}
		}

		return true;
	}

	private static void FitFillColumn(DataGrid grid, Fit fit)
	{
		var fill = grid.Columns.FirstOrDefault(column => column.IsVisible && GetIsFill(column));
		if (fill is null || ViewportOf(grid, fit) is not { } viewport)
		{
			return;
		}

		var others = grid.Columns
			.Where(column => column.IsVisible && column != fill)
			.Sum(column => column.ActualWidth);
		if (viewport == fit.LastViewport && others == fit.LastOthers)
		{
			return;
		}

		fit.LastViewport = viewport;
		fit.LastOthers = others;
		var target = Math.Max(fill.MinWidth, viewport - others);
		if (Math.Abs(target - fill.Width.Value) > FitTolerance)
		{
			fill.Width = new DataGridLength(target);
		}
	}

	/// <summary>The headers presenter's width less the vertical scroll bar's while that bar is visible.</summary>
	private static double? ViewportOf(DataGrid grid, Fit fit)
	{
		if (fit.HeadersPresenter?.FindAncestorOfType<DataGrid>() != grid)
		{
			FindTemplateParts(grid, fit);
		}

		if (fit.HeadersPresenter is not { } headersPresenter)
		{
			return null;
		}

		var scrollBarWidth = fit.VerticalScrollBar is { IsVisible: true } bar ? bar.Bounds.Width : 0;

		return headersPresenter.Bounds.Width - scrollBarWidth;
	}

	private static void FindTemplateParts(DataGrid grid, Fit fit)
	{
		var parts = grid.GetVisualDescendants().OfType<Control>().ToList();
		fit.HeadersPresenter = parts.OfType<DataGridColumnHeadersPresenter>()
			.FirstOrDefault(part => part.Name == "PART_ColumnHeadersPresenter");
		fit.VerticalScrollBar = parts.OfType<ScrollBar>().FirstOrDefault(part => part.Name == "PART_VerticalScrollbar");
	}

	private sealed class Fit
	{
		public bool HeadersMeasured { get; set; }

		public double LastViewport { get; set; } = double.NaN;

		public double LastOthers { get; set; } = double.NaN;

		public DataGridColumnHeadersPresenter? HeadersPresenter { get; set; }

		public ScrollBar? VerticalScrollBar { get; set; }
	}
}
