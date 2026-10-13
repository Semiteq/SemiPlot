namespace SemiPlot.UI.Chart;

public static class ChartPenPick
{
	private const double PickTolerancePixels = 6;

	public static int? Choose(IEnumerable<(int PenId, double Distance)> candidates, int activePenId)
	{
		int? chosen = null;
		var chosenDistance = double.PositiveInfinity;

		foreach (var (penId, distance) in candidates)
		{
			if (distance > PickTolerancePixels)
			{
				continue;
			}

			if (distance < chosenDistance || (distance == chosenDistance && penId == activePenId))
			{
				chosen = penId;
				chosenDistance = distance;
			}
		}

		return chosen;
	}
}
