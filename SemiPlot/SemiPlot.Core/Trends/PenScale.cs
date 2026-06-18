namespace SemiPlot.Core.Trends;

public sealed record PenScale(
	string AxisKey,
	IReadOnlyList<long> PenIds,
	double Min,
	double Max,
	ScaleMode Mode,
	bool IsActive,
	bool IsVisible,
	bool IsLogarithmic);
