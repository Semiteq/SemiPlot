namespace SemiPlot.UI.Chart;

// The single conversion boundary between the UTC time domain and the chart's local-time axis, so plotted X,
// axis limits, cursor X and the navigation window never double-convert. Not perfectly invertible across a DST
// boundary: a UTC->local->UTC round-trip inside the repeated/skipped hour can shift by up to one hour.
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
