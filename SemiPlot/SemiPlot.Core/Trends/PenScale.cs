namespace SemiPlot.Core.Trends;

// A computed Y range for one axis (a shared group or a single pen).
public sealed record PenScale(
	string AxisKey,
	IReadOnlyList<long> PenIds,
	double Min,
	double Max,
	ScaleMode Mode,
	bool IsActive,
	bool IsVisible,
	bool IsLogarithmic);
