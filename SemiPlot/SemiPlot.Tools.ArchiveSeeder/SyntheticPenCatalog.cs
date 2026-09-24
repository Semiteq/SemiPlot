using SemiPlot.Core.Trends;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class SyntheticPenCatalog
{
	// The pen in two groups, the pen the operator kept out of the startup set, and the pen nobody
	// commissioned: the three catalogue states the round-robin slice would otherwise never carry.
	public const int TwoGroupPenId = 1000;

	public const int HiddenOnStartPenId = 2001;

	public const int UncommissionedPenId = 5001;

	private const string HeatersGroup = "Heaters";
	private const string DampersGroup = "Dampers";
	private const string GasLinesGroup = "Gas lines";
	private const string PressuresGroup = "Pressures";
	private const string PowersGroup = "Powers";

	// The second group of the two-group pen; it keeps whatever group Build already gave that pen.
	private const string WatchlistGroup = "Watchlist";

	// Twelve keeps the standard slice's eight round-robin pens on distinct colours.
	private static readonly string[] _palette =
	[
		"#4E79A7", "#F28E2B", "#E15759", "#76B7B2", "#59A14F", "#EDC948",
		"#B07AA1", "#FF9DA7", "#9C755F", "#17BECF", "#D62728", "#9467BD"
	];

	public static IReadOnlyList<SyntheticPen> Build()
	{
		var pens = new List<SyntheticPen>();

		AddGroup(
			pens: pens,
			group: HeatersGroup,
			count: 16,
			idBase: 1000,
			namePrefix: "Heater",
			range: _ => (20.0, 850.0),
			unit: "degC",
			format: "0.0");
		AddGroup(
			pens: pens,
			group: DampersGroup,
			count: 16,
			idBase: 2000,
			namePrefix: "Damper",
			range: _ => (0.0, 100.0),
			unit: "%",
			format: "0",
			lineStyle: PenLineStyle.Stepped);
		AddGroup(
			pens: pens,
			group: GasLinesGroup,
			count: 10,
			idBase: 3000,
			namePrefix: "Gas line",
			range: GasLineRange,
			unit: "sccm",
			format: "0.00");
		AddGroup(
			pens: pens,
			group: PressuresGroup,
			count: 4,
			idBase: 4000,
			namePrefix: "Pressure",
			range: _ => (0.9, 1.4),
			unit: "bar",
			format: "0.000");
		AddGroup(
			pens: pens,
			group: PowersGroup,
			count: 4,
			idBase: 5000,
			namePrefix: "Power",
			range: _ => (0.0, 50.0),
			unit: "kW",
			format: "0.0");

		return [.. pens.Select(Commission)];
	}

	private static SyntheticPen Commission(SyntheticPen pen)
	{
		return pen.PenId switch
		{
			TwoGroupPenId => pen with { Groups = [.. pen.Groups, WatchlistGroup] },
			HiddenOnStartPenId => pen with { EnabledOnStart = false },
			UncommissionedPenId => pen with { Groups = [], Unit = null, Format = null, StoresScale = false },
			_ => pen
		};
	}

	private static void AddGroup(
		List<SyntheticPen> pens,
		string group,
		int count,
		int idBase,
		string namePrefix,
		Func<int, (double Min, double Max)> range,
		string unit,
		string format,
		PenLineStyle lineStyle = PenLineStyle.Interpolated)
	{
		for (var index = 0; index < count; index++)
		{
			var penId = idBase + index;
			var name = $"{namePrefix} {index + 1:00}";
			var color = _palette[pens.Count % _palette.Length];
			var (minValue, maxValue) = range(index);

			pens.Add(new SyntheticPen(
				penId,
				name,
				[group],
				color,
				minValue,
				maxValue,
				unit,
				format,
				LineStyle: lineStyle));
		}
	}

	// Gas lines deliberately use heterogeneous ranges so the multi-axis use case is exercised.
	private static (double Min, double Max) GasLineRange(int index)
	{
		var span = 5.0 + (index * 12.0);
		var min = index * 2.0;

		return (min, min + span);
	}
}
