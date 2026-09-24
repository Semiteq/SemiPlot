namespace SemiPlot.Core.Trends;

public sealed record Pen(
	int PenId,
	string Name,
	IReadOnlyList<string> Groups,
	string Color,
	string? Unit = null,
	string? Format = null,
	bool EnabledOnStart = true,
	double? ScaleMin = null,
	double? ScaleMax = null,
	PenLineStyle LineStyle = PenLineStyle.Interpolated);
