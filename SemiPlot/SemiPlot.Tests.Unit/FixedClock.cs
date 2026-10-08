namespace SemiPlot.Tests.Unit;

internal sealed class FixedClock(DateTimeOffset now, TimeZoneInfo localTimeZone) : TimeProvider
{
	public override TimeZoneInfo LocalTimeZone => localTimeZone;

	public override DateTimeOffset GetUtcNow()
	{
		return now;
	}
}
