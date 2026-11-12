namespace SemiPlot.Core.Data;

/// <summary>
/// Converts between UTC and the archive's naive wall-clock time (docs/architecture/data-integration.md).
/// </summary>
public static class ArchiveClock
{
	/// <summary>The wall-clock time of the instant; the input's own <see cref="DateTime.Kind"/> is ignored.</summary>
	public static DateTime WallClockOf(DateTime utc, TimeZoneInfo zone)
	{
		var wallClock = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

		return DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
	}

	public static DateOnly DayOf(DateTime utc, TimeZoneInfo zone)
	{
		return DateOnly.FromDateTime(WallClockOf(utc, zone));
	}

	public static DateTime Midnight(DateOnly day)
	{
		return day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
	}

	/// <summary>
	/// The instant of a wall-clock time; a time the spring change skips takes the zone's base offset.
	/// </summary>
	public static DateTime ToUtc(DateTime wallClock, TimeZoneInfo zone)
	{
		var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
		if (zone.IsInvalidTime(unspecified))
		{
			return DateTime.SpecifyKind(unspecified - zone.BaseUtcOffset, DateTimeKind.Utc);
		}

		return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
	}
}
