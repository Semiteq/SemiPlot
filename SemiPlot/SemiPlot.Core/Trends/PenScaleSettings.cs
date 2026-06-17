namespace SemiPlot.Core.Trends;

// Operator-controlled scaling configuration for a single pen; pens sharing an AxisKey scale together.
public sealed record PenScaleSettings(
	long PenId,
	string AxisKey,
	ScaleMode Mode = ScaleMode.Auto,
	bool IsVisible = true,
	bool IsLogarithmic = false,
	double ManualMin = 0.0,
	double ManualMax = 1.0);
