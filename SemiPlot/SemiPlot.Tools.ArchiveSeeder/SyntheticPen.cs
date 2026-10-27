using SemiPlot.Core.Trends;

namespace SemiPlot.Tools.ArchiveSeeder;

public sealed record SyntheticPen(
	int PenId,
	string Name,
	IReadOnlyList<string> Groups,
	string Color,
	double MinValue,
	double MaxValue,
	string? Unit = null,
	string? Format = null,
	bool EnabledOnStart = true,
	bool StoresScale = true,
	bool LogScaleOnStart = false,
	PenLineStyle LineStyle = PenLineStyle.Interpolated,
	TimeSpan? ChangePeriod = null)
{
	/// <summary>Lattice steps per value change: one, or the change period's whole number of intervals.</summary>
	public long ChangeStride(long intervalTicks)
	{
		return ChangePeriod is { } period ? Math.Max(1L, period.Ticks / intervalTicks) : 1L;
	}

	/// <summary>The round-robin key: the first group, and the empty string for an ungrouped pen.</summary>
	public string PrimaryGroup => Groups.Count > 0 ? Groups[0] : string.Empty;

	/// <summary>The commissioned lower bound, or none for a pen the operator left autoscaling.</summary>
	public double? ScaleMinOnStart => StoresScale ? MinValue : null;

	/// <summary>The commissioned upper bound, or none for a pen the operator left autoscaling.</summary>
	public double? ScaleMaxOnStart => StoresScale ? MaxValue : null;
}
