namespace SemiPlot.Core.Trends;

// A computed Y range for one axis. AxisKey identifies the axis (a shared group or a single pen);
// PenIds lists every pen that scales against it. IsActive marks the axis carrying the active pen,
// which is the one shown on the primary axis; non-active axes are hidden. IsVisible reflects whether
// any contributing pen is visible. Mode echoes the scaling mode that produced (Min, Max).
public sealed record PenScale(
	string AxisKey,
	IReadOnlyList<long> PenIds,
	double Min,
	double Max,
	ScaleMode Mode,
	bool IsActive,
	bool IsVisible,
	bool IsLogarithmic);
