using SemiPlot.Core.Data;

namespace SemiPlot.Core.Trends;

/// <summary>The chart window on the detail track, clipped to the page; a cut edge draws no border.</summary>
public readonly record struct PageWindowBox(double Left, double Width, bool CutsLeft, bool CutsRight);

/// <summary>A recipe run's shade on a Day page; an interrupted run ending on the page draws an edge there.</summary>
public readonly record struct RunShade(double Left, double Width, bool HasEndEdge);

public enum TrackTickKind
{
	Hour,
	Day
}

/// <summary>A tick at <see cref="X"/> for the archive wall-clock time the UI formats by its kind.</summary>
public readonly record struct TrackTick(double X, DateTime WallClock, TrackTickKind Kind);

/// <summary>The minimap's detail track over one page: the window box, the run shades and the page ticks.</summary>
public static class MinimapTrackGeometry
{
	private static readonly int[] _hourStrides = [1, 2, 3, 4, 6, 12];
	private static readonly int[] _dayStrides = [1, 2, 7];

	/// <summary>The chart window over the page's width, clipped at its edges; null when it misses the page.</summary>
	public static PageWindowBox? WindowBox(
		MinimapPage page,
		DateTime windowFromUtc,
		DateTime windowToUtc,
		double width,
		double minimumWidth)
	{
		if (width <= 0.0 || windowToUtc <= page.FromUtc || windowFromUtc >= page.ToUtc)
		{
			return null;
		}

		var pageSpan = page.ToUtc - page.FromUtc;
		var startFraction = Math.Max((windowFromUtc - page.FromUtc) / pageSpan, 0.0);
		var endFraction = Math.Min((windowToUtc - page.FromUtc) / pageSpan, 1.0);
		var boxWidth = Math.Min(width, Math.Max(minimumWidth, (endFraction - startFraction) * width));
		var left = MinimapGeometry.SpanLeftWithin(startFraction * width, boxWidth, width);

		return new PageWindowBox(left, boxWidth, windowFromUtc < page.FromUtc, windowToUtc > page.ToUtc);
	}

	/// <summary>The shade of every run that meets a Day page, clipped at its edges; a Week page shades none.</summary>
	public static IReadOnlyList<RunShade> RunShades(
		MinimapPage page,
		IReadOnlyList<RecipeRun> runs,
		double width,
		double minimumWidth)
	{
		var shades = new List<RunShade>();
		if (page.Unit != MinimapPageUnit.Day)
		{
			return shades;
		}

		foreach (var run in runs)
		{
			if (WindowBox(page, run.StartUtc, run.EndUtc, width, minimumWidth) is { } box)
			{
				shades.Add(new RunShade(box.Left, box.Width, run.IsInterrupted && !box.CutsRight));
			}
		}

		return shades;
	}

	/// <summary>Whole hours on a Day page, midnights on a Week or Month page, thinned to the minimum spacing.</summary>
	public static IReadOnlyList<TrackTick> PageTicks(
		MinimapPage page,
		double width,
		double minimumSpacing,
		TimeZoneInfo zone)
	{
		var pageSpan = page.ToUtc - page.FromUtc;
		if (width <= 0.0 || pageSpan <= TimeSpan.Zero)
		{
			return [];
		}

		return page.Unit == MinimapPageUnit.Day
			? HourTicks(page, width, minimumSpacing, zone)
			: DayTicks(page, width, minimumSpacing, zone);
	}

	/// <summary>Steps in UTC and keeps ticks a stride of real hours apart across a skipped or repeated hour.</summary>
	private static List<TrackTick> HourTicks(MinimapPage page, double width, double minimumSpacing, TimeZoneInfo zone)
	{
		var pixelsPerHour = width / (page.ToUtc - page.FromUtc).TotalHours;
		var stride = Stride(pixelsPerHour, minimumSpacing, _hourStrides);
		var strideSpan = TimeSpan.FromHours(stride);
		var ticks = new List<TrackTick>();
		var nextAllowedUtc = page.FromUtc;
		for (var utc = page.FromUtc; utc < page.ToUtc; utc = utc.AddHours(1))
		{
			var wallClock = ArchiveClock.WallClockOf(utc, zone);
			if (utc >= nextAllowedUtc && wallClock.Minute == 0 && wallClock.Hour % stride == 0
				&& PageX(page, utc, width) is { } x)
			{
				ticks.Add(new TrackTick(x, wallClock, TrackTickKind.Hour));
				nextAllowedUtc = utc + strideSpan;
			}
		}

		return ticks;
	}

	private static List<TrackTick> DayTicks(MinimapPage page, double width, double minimumSpacing, TimeZoneInfo zone)
	{
		var dayCount = page.EndDay.DayNumber - page.FirstDay.DayNumber;
		var stride = Stride(width / dayCount, minimumSpacing, _dayStrides);
		var ticks = new List<TrackTick>();
		for (var index = 0; index < dayCount; index += stride)
		{
			var midnight = ArchiveClock.Midnight(page.FirstDay.AddDays(index));
			if (PageX(page, ArchiveClock.ToUtc(midnight, zone), width) is { } x)
			{
				ticks.Add(new TrackTick(x, midnight, TrackTickKind.Day));
			}
		}

		return ticks;
	}

	/// <summary>The smallest stride whose units sit at least the minimum spacing apart, else the largest.</summary>
	private static int Stride(double pixelsPerUnit, double minimumSpacing, int[] strides)
	{
		foreach (var stride in strides)
		{
			if (stride * pixelsPerUnit >= minimumSpacing)
			{
				return stride;
			}
		}

		return strides[^1];
	}

	private static double? PageX(MinimapPage page, DateTime utc, double width)
	{
		return MinimapGeometry.FractionAt(page.FromUtc, page.ToUtc, utc) * width;
	}
}
