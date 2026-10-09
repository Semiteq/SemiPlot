using ScottPlot;
using ScottPlot.TickGenerators;

namespace SemiPlot.UI.Chart;

/// <summary>ScottPlot's linear ticks for a drawn axis, reused while the range, the length and the font stand.</summary>
public sealed class LinearTickGenerator : IDrawnTickGenerator
{
	private readonly NumericAutomatic _numeric = new();
	private readonly TickCache<TickKey> _cache = new();

	public bool IsDrawn { get; set; }

	public Tick[] Ticks { get; private set; } = [];

	public int MaxTickCount
	{
		get => _numeric.MaxTickCount;
		set => _numeric.MaxTickCount = value;
	}

	public void Regenerate(CoordinateRange range, Edge edge, PixelLength size, Paint paint, LabelStyle labelStyle)
	{
		if (!IsDrawn)
		{
			Ticks = [];
			return;
		}

		var key = new TickKey(range.Min, range.Max, size.Length, labelStyle.FontSize);

		if (!_cache.TryFind(key, out var ticks))
		{
			_numeric.Regenerate(range, edge, size, paint, labelStyle);
			ticks = _numeric.Ticks;
			_cache.Add(key, ticks);
		}

		Ticks = ticks;
	}

	private readonly record struct TickKey(double Min, double Max, float PixelLength, float FontSize);
}
