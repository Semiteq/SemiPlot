using System.Globalization;

using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>Ticks of a log10 axis whose limits are in decades; majors read under the pen's mask.</summary>
public sealed class LogTickGenerator : IDrawnTickGenerator
{
	private const double LabelTolerance = 0.01;
	private const NumberStyles LabelStyles = NumberStyles.Float | NumberStyles.AllowThousands;

	private readonly TickCache<TickKey> _cache = new();

	// Written on the UI thread by ChartAxisBinder.Apply, read on the render thread in Regenerate; a reference
	// write is atomic, so a frame sees either mask whole.
	public string? Mask { get; set; }

	public bool IsDrawn { get; set; }

	public Tick[] Ticks { get; private set; } = [];

	public int MaxTickCount { get; set; } = int.MaxValue;

	public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
	{
		if (!IsDrawn)
		{
			Ticks = [];
			return;
		}

		var key = new TickKey(range.Min, range.Max, size.Length, Mask);

		if (!_cache.TryFind(key, out var ticks))
		{
			ticks = Generate(key);
			_cache.Add(key, ticks);
		}

		Ticks = ticks;
	}

	/// <summary>An axis runs in decades while this generator ticks it; ScottPlot leaves an unbound axis null.</summary>
	internal static bool IsLogarithmic(IYAxis? axis)
	{
		return axis?.TickGenerator is LogTickGenerator;
	}

	private static Tick[] Generate(TickKey key)
	{
		var positions = LogAxis.Ticks(key.MinLog, key.MaxLog, key.PixelLength);
		var zeroLabel = PenValueFormat.Format(0.0, key.Mask);
		var ticks = new Tick[positions.Count];
		string? lastLabel = null;

		for (var index = 0; index < positions.Count; index++)
		{
			var position = positions[index].Position;
			if (!positions[index].IsMajor)
			{
				ticks[index] = Tick.Minor(position);
				continue;
			}

			var value = Math.Pow(10.0, position);
			var label = PenValueFormat.Format(value, key.Mask);

			// docs/architecture/charting.md#log10-y-axis
			if (label == zeroLabel || label == lastLabel || !ReadsBackAs(label, value))
			{
				ticks[index] = Tick.Minor(position);
				continue;
			}

			ticks[index] = Tick.Major(position, label);
			lastLabel = label;
		}

		return ticks;
	}

	/// <summary>True when the label parses within 1 % of the value, or carries literals no parse reads.</summary>
	private static bool ReadsBackAs(string label, double value)
	{
		return !double.TryParse(label, LabelStyles, CultureInfo.CurrentCulture, out var read)
			|| Math.Abs(read - value) <= value * LabelTolerance;
	}

	private readonly record struct TickKey(double MinLog, double MaxLog, float PixelLength, string? Mask);
}
