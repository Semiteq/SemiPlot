using ScottPlot;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>The active pen's value at every marker in the window: a dot on the pen's line and the number beside it.</summary>
internal sealed class ChartMarkerReadings : IPlottable
{
	private const float DotRadius = 3.5f;
	private const float TextOffsetX = 6f;
	private const float TextFontSize = 12f;
	private const float TextPadding = 2f;
	private const double TextBackgroundOpacity = 0.85;

	// The UI thread writes the readings and the colours in Update and ApplyPalette; the render thread reads them in
	// Render. Every access runs under the lock.
	private readonly Lock _readingsLock = new();
	private readonly List<MarkerReading> _readings = [];
	private readonly LabelStyle _text = new()
	{
		FontSize = TextFontSize,
		Alignment = Alignment.MiddleLeft,
		OffsetX = TextOffsetX,
		Padding = TextPadding
	};

	private readonly FillStyle _dot = new();

	private EnvelopeLine? _penLine;

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public IEnumerable<LegendItem> LegendItems => [];

	/// <summary>The readings of the last <see cref="Update"/>.</summary>
	internal IReadOnlyList<MarkerReading> SnapshotReadings()
	{
		lock (_readingsLock)
		{
			return [.. _readings];
		}
	}

	public AxisLimits GetAxisLimits()
	{
		return AxisLimits.NoLimits;
	}

	/// <summary>Runs on every frame; <paramref name="readAt"/> gives the pen's value at a moment, or null.</summary>
	public void Update(
		IReadOnlyList<StoredMarker> markers,
		TrendPenState? pen,
		Func<DateTime, double?> readAt,
		DateTime fromUtc,
		DateTime toUtc)
	{
		lock (_readingsLock)
		{
			_readings.Clear();
			_penLine = pen?.Line;
			if (pen is null)
			{
				return;
			}

			_dot.Color = new Color(pen.Pen.Color);
			foreach (var marker in markers)
			{
				var isInWindow = marker.TimeUtc >= fromUtc && marker.TimeUtc <= toUtc;
				if (isInWindow && readAt(marker.TimeUtc) is { } value)
				{
					_readings.Add(new MarkerReading(
						marker.Id,
						LocalTimeAxis.ToAxis(marker.TimeUtc),
						value,
						PenValueFormat.Format(value, pen.Pen.Format)));
				}
			}
		}
	}

	public void ApplyPalette(MarkerPalette palette)
	{
		lock (_readingsLock)
		{
			_text.ForeColor = palette.Label;
			_text.BackgroundColor = palette.ReadingBackground.WithOpacity(TextBackgroundOpacity);
		}
	}

	public void Render(RenderPack rp)
	{
		lock (_readingsLock)
		{
			if (_penLine is not { } penLine)
			{
				return;
			}

			foreach (var reading in _readings)
			{
				var pixel = penLine.PixelOf(reading.X, reading.Value, rp.DataRect);
				if (!rp.DataRect.Contains(pixel))
				{
					continue;
				}

				Drawing.FillCircle(rp.Canvas, pixel, DotRadius, _dot, rp.Paint);
				_text.Text = reading.Text;
				_text.Render(rp.Canvas, pixel, rp.Paint);
			}
		}
	}
}

internal readonly record struct MarkerReading(long MarkerId, double X, double Value, string Text);
