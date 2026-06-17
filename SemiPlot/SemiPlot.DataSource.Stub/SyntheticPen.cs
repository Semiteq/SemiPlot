using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Stub;

public sealed record SyntheticPen(
	long ProjectVarId,
	string Name,
	string Group,
	string Color,
	double MinValue,
	double MaxValue,
	PenLineStyle LineStyle = PenLineStyle.Interpolated)
{
	public Pen ToPen()
	{
		return new(ProjectVarId, Name, Group, Color, LineStyle);
	}
}
