namespace SemiPlot.Core.Trends;

public sealed record PenScaleSettings(
	int PenId,
	ScaleMode Mode = ScaleMode.Auto,
	bool IsLogarithmic = false,
	double ManualMin = 0.0,
	double ManualMax = 1.0);
