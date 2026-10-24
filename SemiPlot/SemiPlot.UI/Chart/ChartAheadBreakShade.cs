using ScottPlot;

using SemiPlot.UI.Localization;

using SkiaSharp;

namespace SemiPlot.UI.Chart;

/// <summary>A grey band over the drawn pen's ahead-break spans, under every pen line, labelled where it fits.</summary>
internal sealed class ChartAheadBreakShade : IPlottable, IDisposable
{
	private const float LabelFontSize = 12f;
	private const float LabelBottomOffset = 4f;
	private const float LabelSideMargin = 4f;

	// The UI thread writes the line and the colours in ShowFor and ApplyPalette; the render thread reads them in
	// Render. Every access runs under the lock, which Render holds while it takes the line's.
	private readonly Lock _shadeLock = new();
	private readonly List<AheadBreakSpan> _spans = [];
	private readonly FillStyle _fill = new();
	private readonly LabelStyle _label = new()
	{
		Text = Resources.ChartAheadBreakLabel,
		FontSize = LabelFontSize
	};

	private EnvelopeLine? _line;

	// docs/architecture/charting.md#the-ahead-break-shade
	private SKTextBlob? _labelGlyphs;
	private float _labelWidth;
	private float _labelDescent;

	public bool IsVisible { get; set; } = true;

	public IAxes Axes { get; set; } = new Axes();

	public IEnumerable<LegendItem> LegendItems => [];

	internal EnvelopeLine? ShownLine
	{
		get
		{
			lock (_shadeLock)
			{
				return _line;
			}
		}
	}

	public AxisLimits GetAxisLimits()
	{
		return AxisLimits.NoLimits;
	}

	/// <summary>Runs on every frame; null shades nothing.</summary>
	public void ShowFor(EnvelopeLine? drawnLine)
	{
		lock (_shadeLock)
		{
			_line = drawnLine;
		}
	}

	public void ApplyPalette(AheadBreakPalette palette)
	{
		lock (_shadeLock)
		{
			_fill.Color = palette.Fill;
			_label.ForeColor = palette.Label;
		}
	}

	public void Render(RenderPack rp)
	{
		lock (_shadeLock)
		{
			if (_line is not { } line)
			{
				return;
			}

			line.CollectAheadBreakSpans(_spans);
			if (_spans.Count == 0)
			{
				return;
			}

			var dataRect = rp.DataRect;
			foreach (var span in _spans)
			{
				var left = Math.Max(dataRect.Left, Axes.XAxis.GetPixel(span.FromX, dataRect));
				var right = Math.Min(dataRect.Right, Axes.XAxis.GetPixel(span.ToX, dataRect));
				if (!(right > left))
				{
					continue;
				}

				var band = new PixelRect(left, right, dataRect.Bottom, dataRect.Top);
				_fill.Render(rp, band);
				DrawLabelIfItFits(rp, band);
			}
		}
	}

	public void Dispose()
	{
		lock (_shadeLock)
		{
			_labelGlyphs?.Dispose();
			_labelGlyphs = null;
		}
	}

	private void DrawLabelIfItFits(RenderPack rp, PixelRect band)
	{
		_label.ApplyToPaint(rp.Paint);
		var glyphs = _labelGlyphs ??= ShapeLabel(rp.Paint);
		if (glyphs is null || band.Width < _labelWidth + (2f * LabelSideMargin))
		{
			return;
		}

		var baselineY = rp.DataRect.Bottom - LabelBottomOffset - _labelDescent;
		rp.Canvas.DrawText(glyphs, band.HorizontalCenter - (_labelWidth / 2f), baselineY, rp.Paint.SKPaint);
	}

	/// <summary>Measures and shapes the label once, with the font the label style set on the paint.</summary>
	private SKTextBlob? ShapeLabel(Paint paint)
	{
		_labelWidth = paint.MeasureText(_label.Text).Width;
		_labelDescent = paint.GetFontMetrics().metrics.Bottom;

		return SKTextBlob.Create(_label.Text, paint.SKFont);
	}
}
