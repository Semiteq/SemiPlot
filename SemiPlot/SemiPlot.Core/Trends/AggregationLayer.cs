namespace SemiPlot.Core.Trends;

// Mirrors the Simple-Scada archive layer codes.
public enum AggregationLayer
{
	Raw,
	Minute,
	Hour,
	Day
}

public static class AggregationLayerExtensions
{
	public static TimeSpan ToSampleInterval(this AggregationLayer layer)
	{
		return layer switch
		{
			AggregationLayer.Raw => TimeSpan.FromSeconds(1),
			AggregationLayer.Minute => TimeSpan.FromMinutes(1),
			AggregationLayer.Hour => TimeSpan.FromHours(1),
			AggregationLayer.Day => TimeSpan.FromDays(1),
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown aggregation layer.")
		};
	}
}
