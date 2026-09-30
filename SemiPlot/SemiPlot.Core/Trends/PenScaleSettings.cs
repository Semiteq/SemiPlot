namespace SemiPlot.Core.Trends;

public sealed record PenScaleSettings(
	int PenId,
	ScaleMode Mode = ScaleMode.Auto,
	bool IsLogarithmic = false,
	double ManualMin = 0.0,
	double ManualMax = 1.0)
{
	public static PenScaleSettings InitialFor(Pen pen)
	{
		var settings = new PenScaleSettings(pen.PenId);

		if (pen.ScaleMin is { } min && pen.ScaleMax is { } max)
		{
			settings = settings with { Mode = ScaleMode.Manual, ManualMin = min, ManualMax = max };
		}

		return settings;
	}
}
