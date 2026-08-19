namespace SemiPlot.Core.Trends;

// Mirrors the Simple-Scada archive layer codes. The values are explicit because they are bound to the
// archive's l column directly, so a member inserted between two others would read the wrong layer.
public enum AggregationLayer
{
	Raw = 0,
	Minute = 1,
	Hour = 2,
	Day = 3
}

public static class AggregationLayerExtensions
{
	// Distance between two consecutive points of a layer. The archive writes up to four points into
	// every period, so a layer's spacing is a quarter of its period, not the period itself.
	// Raw has no period; its true spacing is the per-variable archiving interval, so one second stands
	// in for it and never participates in layer selection.
	public static TimeSpan ToPointSpacing(this AggregationLayer layer)
	{
		return layer switch
		{
			AggregationLayer.Raw => TimeSpan.FromSeconds(1),
			AggregationLayer.Minute => TimeSpan.FromSeconds(15),
			AggregationLayer.Hour => TimeSpan.FromMinutes(15),
			AggregationLayer.Day => TimeSpan.FromHours(6),
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown aggregation layer.")
		};
	}
}
