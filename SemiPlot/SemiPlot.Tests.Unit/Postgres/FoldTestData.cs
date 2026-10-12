using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

namespace SemiPlot.Tests.Unit.Postgres;

/// <summary>UTC is the zone unless the conversion is the subject.</summary>
internal static class FoldTestData
{
	public static readonly ArchiveTimeConverter UtcConverter = new(TimeZoneInfo.Utc);

	public static readonly ArchiveTimeConverter BerlinConverter =
		new(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

	public static DateTime Local(int hour, int minute)
	{
		return new DateTime(2026, 6, 15, hour, minute, 0, DateTimeKind.Unspecified);
	}

	public static DateTime Utc(int hour, int minute)
	{
		return new DateTime(2026, 6, 15, hour, minute, 0, DateTimeKind.Utc);
	}

	/// <summary>The 2026 EU fall-back day, where 02:00 to 03:00 is read twice.</summary>
	public static DateTime FallBackLocal(int hour, int minute)
	{
		return new DateTime(2026, 10, 25, hour, minute, 0, DateTimeKind.Unspecified);
	}

	/// <summary>Counts columns: two wrong folds also put a NaN between the markers.</summary>
	public static int NaNColumnCount(PenHistoryEnvelope envelope)
	{
		return envelope.Center.Count(double.IsNaN);
	}
}
