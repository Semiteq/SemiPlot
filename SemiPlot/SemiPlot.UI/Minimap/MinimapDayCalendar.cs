using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using SemiPlot.Core.Trends;

using Pen = Avalonia.Media.Pen;

namespace SemiPlot.UI.Minimap;

/// <summary>
/// The day calendar's cells, month separators, page outline, hovered cell and marker ticks
/// (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
public sealed class MinimapDayCalendar : Control
{
	public static readonly StyledProperty<IReadOnlyList<CalendarCell>> CellsProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IReadOnlyList<CalendarCell>>(nameof(Cells), []);

	public static readonly StyledProperty<TrackSpan?> PageOutlineProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, TrackSpan?>(nameof(PageOutline));

	public static readonly StyledProperty<TrackSpan?> HoveredCellProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, TrackSpan?>(nameof(HoveredCell));

	public static readonly StyledProperty<IReadOnlyList<TrackSpan>> MarkerTicksProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IReadOnlyList<TrackSpan>>(nameof(MarkerTicks), []);

	public static readonly StyledProperty<IReadOnlyList<double>> MonthSeparatorsProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IReadOnlyList<double>>(nameof(MonthSeparators), []);

	public static readonly StyledProperty<IBrush?> PresenceBrushProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IBrush?>(nameof(PresenceBrush));

	public static readonly StyledProperty<IBrush?> AccentBrushProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IBrush?>(nameof(AccentBrush));

	public static readonly StyledProperty<IBrush?> HoverBrushProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IBrush?>(nameof(HoverBrush));

	public static readonly StyledProperty<IBrush?> SeparatorBrushProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IBrush?>(nameof(SeparatorBrush));

	public static readonly StyledProperty<IBrush?> MarkerBrushProperty =
		AvaloniaProperty.Register<MinimapDayCalendar, IBrush?>(nameof(MarkerBrush));

	// The cells sit under the 2 px marker ticks with room for the outline's stroke between, in the 16 px row.
	private const double CellTop = 4.0;
	private const double CellHeight = 10.0;
	private const double MarkerTickHeight = 2.0;
	private const double HoverStroke = 1.0;

	static MinimapDayCalendar()
	{
		AffectsRender<MinimapDayCalendar>(
			CellsProperty,
			PageOutlineProperty,
			HoveredCellProperty,
			MarkerTicksProperty,
			MonthSeparatorsProperty,
			PresenceBrushProperty,
			AccentBrushProperty,
			HoverBrushProperty,
			SeparatorBrushProperty,
			MarkerBrushProperty);
	}

	public IReadOnlyList<CalendarCell> Cells
	{
		get => GetValue(CellsProperty);
		set => SetValue(CellsProperty, value);
	}

	public TrackSpan? PageOutline
	{
		get => GetValue(PageOutlineProperty);
		set => SetValue(PageOutlineProperty, value);
	}

	public TrackSpan? HoveredCell
	{
		get => GetValue(HoveredCellProperty);
		set => SetValue(HoveredCellProperty, value);
	}

	public IReadOnlyList<TrackSpan> MarkerTicks
	{
		get => GetValue(MarkerTicksProperty);
		set => SetValue(MarkerTicksProperty, value);
	}

	public IReadOnlyList<double> MonthSeparators
	{
		get => GetValue(MonthSeparatorsProperty);
		set => SetValue(MonthSeparatorsProperty, value);
	}

	public IBrush? PresenceBrush
	{
		get => GetValue(PresenceBrushProperty);
		set => SetValue(PresenceBrushProperty, value);
	}

	public IBrush? AccentBrush
	{
		get => GetValue(AccentBrushProperty);
		set => SetValue(AccentBrushProperty, value);
	}

	public IBrush? HoverBrush
	{
		get => GetValue(HoverBrushProperty);
		set => SetValue(HoverBrushProperty, value);
	}

	public IBrush? SeparatorBrush
	{
		get => GetValue(SeparatorBrushProperty);
		set => SetValue(SeparatorBrushProperty, value);
	}

	public IBrush? MarkerBrush
	{
		get => GetValue(MarkerBrushProperty);
		set => SetValue(MarkerBrushProperty, value);
	}

	public override void Render(DrawingContext context)
	{
		if (SeparatorBrush is { } separatorBrush)
		{
			foreach (var x in MonthSeparators)
			{
				context.FillRectangle(separatorBrush, new Rect(x, 0.0, DayCalendar.SeparatorWidth, Bounds.Height));
			}
		}

		if (PresenceBrush is { } presenceBrush)
		{
			foreach (var cell in Cells)
			{
				using (context.PushOpacity(OpacityOf(cell.Coverage)))
				{
					context.FillRectangle(presenceBrush, new Rect(cell.Left, CellTop, cell.Width, CellHeight));
				}
			}
		}

		if (AccentBrush is { } accentBrush && PageOutline is { } outline)
		{
			var stroke = DayCalendar.OutlineStroke;
			var ring = new Rect(outline.Left, CellTop - stroke, outline.Width, CellHeight + (2.0 * stroke));
			context.DrawRectangle(new Pen(accentBrush, stroke), ring.Deflate(stroke / 2.0));
		}

		if (HoverBrush is { } hoverBrush && HoveredCell is { } hovered)
		{
			var cell = new Rect(hovered.Left, CellTop, hovered.Width, CellHeight);
			context.DrawRectangle(new Pen(hoverBrush, HoverStroke), cell.Deflate(HoverStroke / 2.0));
		}

		if (MarkerBrush is { } markerBrush)
		{
			foreach (var tick in MarkerTicks)
			{
				context.FillRectangle(markerBrush, new Rect(tick.Left, 0.0, tick.Width, MarkerTickHeight));
			}
		}
	}

	/// <summary>The fill opacity of a cell: three steps, so a short day reads apart from a full one.</summary>
	private static double OpacityOf(DayCoverage coverage)
	{
		return coverage switch
		{
			DayCoverage.Low => 0.35,
			DayCoverage.Medium => 0.65,
			_ => 1.0
		};
	}
}
