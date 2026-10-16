using System.Globalization;

using ScottPlot;
using ScottPlot.Plottables;

using SemiPlot.Core.Data;

namespace SemiPlot.UI.Chart;

/// <summary>One dashed vertical line per marker on the time axis, labelled inside the data area's top edge.</summary>
internal sealed class ChartMarkerLines(Plot plot)
{
	private const float LineWidth = 1.5f;

	// docs/architecture/trend-interaction.md#the-time-markers
	private const float LabelOffsetX = 4f;
	private const float LabelOffsetY = 12f;
	private const double LabelCharacterWidth = 0.6;
	private const string LabelTimeFormat = "HH:mm:ss";

	private readonly Plot _plot = plot;
	private readonly Dictionary<long, VerticalLine> _lines = [];
	private readonly HashSet<long> _shownIds = [];
	private readonly List<long> _goneIds = [];
	private IReadOnlyList<StoredMarker> _markers = [];
	private MarkerPalette? _palette;

	/// <summary>The line of every marker of the last <see cref="Sync"/>, by marker id.</summary>
	internal IReadOnlyDictionary<long, VerticalLine> Lines => _lines;

	/// <summary>Runs on every pointer move of a marker drag.</summary>
	public void Sync(IReadOnlyList<StoredMarker> markers)
	{
		lock (_plot.Sync)
		{
			foreach (var marker in markers)
			{
				if (!_lines.TryGetValue(marker.Id, out var line))
				{
					line = AddLine();
					_lines.Add(marker.Id, line);
				}

				line.X = LocalTimeAxis.ToAxis(marker.TimeUtc);
				line.LabelText = LabelTextOf(marker);
			}

			var hasGoneLines = _lines.Count > markers.Count;
			if (hasGoneLines)
			{
				RemoveGoneLines(markers);
			}
		}

		_markers = markers;
	}

	public void Project(DateTime fromUtc, DateTime toUtc, double dataAreaWidth)
	{
		var axisFrom = LocalTimeAxis.ToAxis(fromUtc);
		var axisSpan = LocalTimeAxis.ToAxis(toUtc) - axisFrom;
		var pixelsPerAxisUnit = axisSpan > 0.0 ? dataAreaWidth / axisSpan : 0.0;

		lock (_plot.Sync)
		{
			VerticalLine? previous = null;
			var previousPixel = 0.0;
			foreach (var marker in _markers)
			{
				var line = _lines[marker.Id];
				line.IsVisible = marker.TimeUtc >= fromUtc && marker.TimeUtc <= toUtc;
				if (!line.IsVisible)
				{
					continue;
				}

				var pixel = (line.X - axisFrom) * pixelsPerAxisUnit;
				previous?.LabelStyle.IsVisible = pixel - previousPixel >= EstimatedLabelWidth(previous);
				previous = line;
				previousPixel = pixel;
			}

			previous?.LabelStyle.IsVisible = dataAreaWidth > 0.0;
		}
	}

	/// <summary>Recolours every line; a line added later takes the same colours.</summary>
	public void ApplyPalette(MarkerPalette palette)
	{
		_palette = palette;
		lock (_plot.Sync)
		{
			foreach (var line in _lines.Values)
			{
				Paint(line, palette);
			}
		}
	}

	public void RemoveLines()
	{
		lock (_plot.Sync)
		{
			foreach (var line in _lines.Values)
			{
				_plot.Remove(line);
			}
		}

		_lines.Clear();
		_markers = [];
	}

	private void RemoveGoneLines(IReadOnlyList<StoredMarker> markers)
	{
		foreach (var marker in markers)
		{
			_shownIds.Add(marker.Id);
		}

		foreach (var id in _lines.Keys)
		{
			if (!_shownIds.Contains(id))
			{
				_goneIds.Add(id);
			}
		}

		foreach (var goneId in _goneIds)
		{
			_plot.Remove(_lines[goneId]);
			_lines.Remove(goneId);
		}

		_shownIds.Clear();
		_goneIds.Clear();
	}

	private static string LabelTextOf(StoredMarker marker)
	{
		var time = DateTime.SpecifyKind(marker.TimeUtc, DateTimeKind.Utc)
			.ToLocalTime()
			.ToString(LabelTimeFormat, CultureInfo.CurrentCulture);

		return marker.Label is { } label ? $"{label} \u00B7 {time}" : time;
	}

	private static double EstimatedLabelWidth(VerticalLine line)
	{
		return LabelOffsetX + (line.LabelText.Length * line.LabelFontSize * LabelCharacterWidth);
	}

	private static void Paint(VerticalLine line, MarkerPalette palette)
	{
		line.LineColor = palette.Line;
		line.LabelFontColor = palette.Label;
	}

	private VerticalLine AddLine()
	{
		var line = _plot.Add.VerticalLine(0.0);
		line.Axes.XAxis = _plot.Axes.Bottom;
		line.EnableAutoscale = false;
		line.LineWidth = LineWidth;
		line.LinePattern = LinePattern.Dashed;
		line.LabelOppositeAxis = true;
		line.ManualLabelAlignment = Alignment.UpperLeft;
		line.LabelOffsetX = LabelOffsetX;
		line.LabelOffsetY = LabelOffsetY;
		line.LabelBackgroundColor = Colors.Transparent;
		if (_palette is { } palette)
		{
			Paint(line, palette);
		}

		return line;
	}
}
