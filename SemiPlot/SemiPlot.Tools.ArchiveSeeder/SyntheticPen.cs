using SemiPlot.Core.Trends;

namespace SemiPlot.Tools.ArchiveSeeder;

public sealed record SyntheticPen(
	long PenId,
	string Name,
	string Group,
	string Color,
	double MinValue,
	double MaxValue,
	PenLineStyle LineStyle = PenLineStyle.Interpolated)
{
	public Pen ToPen()
	{
		return new(PenId, Name, Group, Color, LineStyle);
	}
}
