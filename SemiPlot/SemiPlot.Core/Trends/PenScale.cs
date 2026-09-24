namespace SemiPlot.Core.Trends;

public sealed record PenScale(
	int PenId,
	double Min,
	double Max,
	ScaleMode Mode,
	bool IsActive,
	bool IsLogarithmic);
