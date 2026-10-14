namespace SemiPlot.Core.Trends;

// Mirrors the Simple-Scada archive layer codes. The values are explicit because they are bound to the
// archive's l column directly, so a member inserted between two others would read the wrong layer.
public enum AggregationLayer
{
	Raw = 0,
	Minute = 1,
	Hour = 2
}

public static class AggregationLayerExtensions
{
	// docs/architecture/data-integration.md#layer-ladder
	public static TimeSpan ToPointSpacing(this AggregationLayer layer)
	{
		return layer switch
		{
			AggregationLayer.Raw => throw new ArgumentOutOfRangeException(
				nameof(layer),
				layer,
				"The raw layer's spacing is the archiving interval, which the archive does not record."),
			AggregationLayer.Minute => TimeSpan.FromSeconds(15),
			AggregationLayer.Hour => TimeSpan.FromMinutes(15),
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown aggregation layer.")
		};
	}
}
