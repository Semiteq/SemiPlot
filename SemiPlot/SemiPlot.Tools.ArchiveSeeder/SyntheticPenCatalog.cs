using SemiPlot.Core.Trends;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class SyntheticPenCatalog
{
	private const string HeatersGroup = "Heaters";
	private const string DampersGroup = "Dampers";
	private const string GasLinesGroup = "Gas lines";
	private const string PressuresGroup = "Pressures";
	private const string PowersGroup = "Powers";

	// Taken in catalogue order. Twelve is what keeps the standard slice's eight round-robin pens — catalogue
	// positions 0, 1, 16, 17, 32, 33, 42 and 46 — on eight distinct colours, and every colour carries the
	// chroma the break-render journey probes for.
	private static readonly string[] _palette =
	[
		"#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F", "#EDC948",
		"#B07AA1", "#FF9DA7", "#9C755F", "#17BECF", "#D62728", "#9467BD"
	];

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
			var color = _palette[pens.Count % _palette.Length];

			pens.Add(new SyntheticPen(penId, name, group, color, minValue, maxValue, lineStyle));
		}
	}

	private static void AddGasLines(List<SyntheticPen> pens, int count, int idBase)
	{
		for (var index = 0; index < count; index++)
		{
			var penId = idBase + index;
			var name = $"Gas line {index + 1:00}";
			var color = _palette[pens.Count % _palette.Length];
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
}
