namespace SemiPlot.AppHost;

/// <summary>The archive the stand seeds: the keys converge seeds it with and the keys the demo writer continues it
/// with, chosen by the launch profile's <see cref="VariableName"/>.</summary>
public sealed record DemoDataset(string Name, IReadOnlyList<string> SeedArguments, IReadOnlyList<string> FollowArguments)
{
	public const string VariableName = "SEMIPLOT_DEMO_DATASET";

	// One density for the seeded day and the live tail, so the chart shows no seam between them.
	public static readonly DemoDataset OneDay = new(
		"one-day",
		["--change-seconds", "0.5"],
		["--change-seconds", "0.5"]);

	// The writer repeats the seed's pens and interval, so the live tail continues the seeded lattice
	// (docs/architecture/bench.md#the-demo-writer).
	public static readonly DemoDataset SixMonths = new(
		"six-months",
		["--schedule", "shifts", "--days", "183", "--pens", "50", "--change-seconds", "30"],
		["--pens", "50", "--change-seconds", "30"]);

	private static readonly DemoDataset[] _all = [OneDay, SixMonths];

	/// <summary>The one-day stand when the variable is unset; an unknown name stops the AppHost.</summary>
	public static DemoDataset FromName(string? name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return OneDay;
		}

		return _all.FirstOrDefault(dataset => dataset.Name == name)
			?? throw new InvalidOperationException(
				$"{VariableName} is '{name}'; the stand seeds "
				+ string.Join(" or ", _all.Select(dataset => $"'{dataset.Name}'")) + ".");
	}
}
