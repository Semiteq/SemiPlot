namespace SemiPlot.UI.Chart;

// The single conversion boundary between the UTC time domain (samples on the wire, the navigation
// model, the cursor/delta readouts) and the chart's time axis, which must display computer-local time.
// Every UTC timestamp becomes an axis coordinate (OADate) through ToAxis, and every axis coordinate
// read back from the renderer (cursor anchor) returns to UTC through FromAxis. Converting in exactly
// one place keeps plotted X, axis limits, cursor X and the navigation window in one consistent local
// domain without ever double-converting.
//
// Limitation (acceptable for the MVP): ToAxis/FromAxis are not perfectly invertible across a DST
// transition. The repeated/skipped local hour at a fall-back/spring-forward boundary maps ambiguously,
// so a UTC→local→UTC round-trip of an instant inside that window can shift by up to one hour. Trend
// display and cursor readouts tolerate this; sub-hour DST-boundary precision is out of scope.
internal static class LocalTimeAxis
{
	public static double ToAxis(DateTime timestampUtc)
	{
		var utc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
		return utc.ToLocalTime().ToOADate();
	}

	public static DateTime FromAxis(double oaDate)
	{
		var local = DateTime.SpecifyKind(DateTime.FromOADate(oaDate), DateTimeKind.Local);
		return local.ToUniversalTime();
	}
}
