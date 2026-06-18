namespace SemiPlot.Core.Trends;

public sealed record Pen(
	long PenId,
	string Name,
	string Group,
	string Color,
	PenLineStyle LineStyle = PenLineStyle.Interpolated);
