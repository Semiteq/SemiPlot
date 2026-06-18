namespace SemiPlot.Core.Trends;

public sealed record PenScaleSettings(
	long PenId,
	string AxisKey,
	ScaleMode Mode = ScaleMode.Auto,
	bool IsVisible = true,
	bool IsLogarithmic = false,
	double ManualMin = 0.0,
	double ManualMax = 1.0);
