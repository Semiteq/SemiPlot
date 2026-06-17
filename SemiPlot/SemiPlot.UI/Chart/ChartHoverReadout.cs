using System.Text;

using ScottPlot;

namespace SemiPlot.UI.Chart;

// View-side on-chart hover readout: a single ScottPlot Text plottable pinned to the shared bottom
// (time) axis (like the cursor line), showing every visible pen's center value at the cursor X plus the
// timestamp. Suppressed during a hand-pan drag or delta mode so only the plain hover X-trace surfaces it.
public sealed class ChartHoverReadout
{
	private const float LabelFontSize = 12f;

	private readonly ScottPlot.Plottables.Text _label;

	public ChartHoverReadout(Plot plot)
	{
		ArgumentNullException.ThrowIfNull(plot);

		_label = plot.Add.Text(string.Empty, new Coordinates(0.0, 0.0));
		// Pin both axes to known instances: X to the shared bottom (time) axis, Y to the primary left axis.
		// Without an explicit YAxis the label defaults to whichever axis ScottPlot picks, so its Y anchor
		// (TopOfPlot reads YAxis.Max) could misplace in a multi-axis layout or before the first render.
		_label.Axes.XAxis = plot.Axes.Bottom;
		_label.Axes.YAxis = plot.Axes.Left;
		_label.LabelStyle.FontSize = LabelFontSize;
		_label.LabelBackgroundColor = Colors.White.WithAlpha(0.85);
		_label.LabelBorderColor = Colors.Black.WithAlpha(0.5);
		_label.LabelBorderWidth = 1f;
		_label.LabelPadding = 4f;
		_label.IsVisible = false;
	}

	public bool IsVisible => _label.IsVisible;

	public string Content => _label.LabelText;

	public void Update(
		DateTime? cursorTime,
		IReadOnlyDictionary<long, double?> values,
		IReadOnlyCollection<TrendPenState> pens,
		bool suppress)
	{
		ArgumentNullException.ThrowIfNull(values);
		ArgumentNullException.ThrowIfNull(pens);

		if (suppress || cursorTime is not { } time)
		{
			_label.IsVisible = false;
			return;
		}

		_label.LabelText = BuildContent(time, values, pens);
		_label.Location = new Coordinates(LocalTimeAxis.ToAxis(time), TopOfPlot());
		_label.IsVisible = true;
	}

	public static string BuildContent(
		DateTime cursorTime,
		IReadOnlyDictionary<long, double?> values,
		IReadOnlyCollection<TrendPenState> pens)
	{
		ArgumentNullException.ThrowIfNull(values);
		ArgumentNullException.ThrowIfNull(pens);

		var content = new StringBuilder();
		content.Append(FormatTimestamp(cursorTime));

		foreach (var pen in pens)
		{
			if (!pen.IsVisible)
			{
				continue;
			}

			var value = values.TryGetValue(pen.Pen.ProjectVarId, out var penValue) ? penValue : null;
			content.Append('\n');
			content.Append(pen.Pen.Name);
			content.Append(": ");
			content.Append(FormatValue(value));
		}

		return content.ToString();
	}

	private double TopOfPlot()
	{
		return _label.Axes.YAxis?.Max ?? 0.0;
	}

	private static string FormatTimestamp(DateTime cursorTimeUtc)
	{
		return DateTime.SpecifyKind(cursorTimeUtc, DateTimeKind.Utc)
			.ToLocalTime()
			.ToString("yyyy-MM-dd HH:mm:ss");
	}

	private static string FormatValue(double? value)
	{
		return value is { } number ? number.ToString("0.###") : "—";
	}
}
