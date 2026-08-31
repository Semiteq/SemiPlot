using SemiPlot.Core.Trends;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class SyntheticPenCatalog
{
	private const string HeatersGroup = "Heaters";
	private const string DampersGroup = "Dampers";
	private const string GasLinesGroup = "Gas lines";
	private const string PressuresGroup = "Pressures";
	private const string PowersGroup = "Powers";

	public static IReadOnlyList<SyntheticPen> Build()
	{
		var pens = new List<SyntheticPen>();

		AddGroup(pens, HeatersGroup, count: 16, idBase: 1000, "Heater", minValue: 20.0, maxValue: 850.0);
		AddGroup(
			pens,
			DampersGroup,
			count: 16,
			idBase: 2000,
			"Damper",
			minValue: 0.0,
			maxValue: 100.0,
			lineStyle: PenLineStyle.Stepped);
		AddGasLines(pens, count: 10, idBase: 3000);
		AddGroup(pens, PressuresGroup, count: 4, idBase: 4000, "Pressure", minValue: 0.9, maxValue: 1.4);
		AddGroup(pens, PowersGroup, count: 4, idBase: 5000, "Power", minValue: 0.0, maxValue: 50.0);

		return pens;
	}

	private static void AddGroup(
		List<SyntheticPen> pens,
		string group,
		int count,
		int idBase,
		string namePrefix,
		double minValue,
		double maxValue,
		PenLineStyle lineStyle = PenLineStyle.Interpolated)
	{
		for (var index = 0; index < count; index++)
		{
			var penId = idBase + index;
			var name = $"{namePrefix} {index + 1:00}";
			var color = ColorFor(penId);

			pens.Add(new SyntheticPen(penId, name, group, color, minValue, maxValue, lineStyle));
		}
	}

	private static void AddGasLines(List<SyntheticPen> pens, int count, int idBase)
	{
		for (var index = 0; index < count; index++)
		{
			var penId = idBase + index;
			var name = $"Gas line {index + 1:00}";
			var color = ColorFor(penId);
			var (rangeMin, rangeMax) = GasLineRange(index);

			pens.Add(new SyntheticPen(penId, name, GasLinesGroup, color, rangeMin, rangeMax));
		}
	}

	// Gas lines deliberately use heterogeneous ranges so the multi-axis use case is exercised.
	private static (double Min, double Max) GasLineRange(int index)
	{
		var span = 5.0 + index * 12.0;
		var min = index * 2.0;

		return (min, min + span);
	}

	// Golden-ratio hue walk spreads pen colors so adjacent ids stay visually distinct.
	private static string ColorFor(int penId)
	{
		const double goldenRatioConjugate = 0.618033988749895;
		var hue = (penId * goldenRatioConjugate) % 1.0;

		return HsvToHex(hue, saturation: 0.65, value: 0.85);
	}

	private static string HsvToHex(double hue, double saturation, double value)
	{
		var sector = hue * 6.0;
		var sectorIndex = (int)Math.Floor(sector) % 6;
		var fractional = sector - Math.Floor(sector);

		var p = value * (1.0 - saturation);
		var q = value * (1.0 - saturation * fractional);
		var t = value * (1.0 - saturation * (1.0 - fractional));

		var (red, green, blue) = sectorIndex switch
		{
			0 => (value, t, p),
			1 => (q, value, p),
			2 => (p, value, t),
			3 => (p, q, value),
			4 => (t, p, value),
			_ => (value, p, q)
		};

		return $"#{ToByte(red):X2}{ToByte(green):X2}{ToByte(blue):X2}";
	}

	private static int ToByte(double channel)
	{
		return (int)Math.Round(Math.Clamp(channel, 0.0, 1.0) * 255.0);
	}
}
