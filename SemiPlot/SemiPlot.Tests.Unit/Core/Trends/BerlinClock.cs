namespace SemiPlot.Tests.Unit.Core.Trends;

/// <summary>Europe/Berlin, the archive zone the minimap's Core tests read their wall clock in.</summary>
internal static class BerlinClock
{
	public static readonly TimeZoneInfo BerlinZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	/// <summary>The instant of a whole wall-clock hour of 2026 in Berlin.</summary>
	public static DateTime Berlin(int month, int day, int hour)
	{
		var wallClock = new DateTime(2026, month, day, hour, 0, 0, DateTimeKind.Unspecified);

		return TimeZoneInfo.ConvertTimeToUtc(wallClock, BerlinZone);
	}
}
