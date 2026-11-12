using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Navigation;

/// <summary>The recipe runs and the operator markers in time order; hidden recipe markers still pair.</summary>
internal sealed record MarkerTargets(IReadOnlyList<RecipeRun> Runs, IReadOnlyList<StoredMarker> OperatorMarkers);

/// <summary>The window each step sets, or null; steps measure from the centre, not before the floor.</summary>
internal sealed record NavigationSteps(
	(DateTime FromUtc, DateTime ToUtc)? PreviousRun,
	(DateTime FromUtc, DateTime ToUtc)? NextRun,
	(DateTime FromUtc, DateTime ToUtc)? LatestRun,
	(DateTime FromUtc, DateTime ToUtc)? PreviousMarker,
	(DateTime FromUtc, DateTime ToUtc)? NextMarker)
{
	public static NavigationSteps Of(
		MarkerTargets targets,
		(DateTime FromUtc, DateTime ToUtc) window,
		DateTime firstSampleUtc)
	{
		var centreUtc = CentreOf(window);
		var referenceUtc = centreUtc < firstSampleUtc ? firstSampleUtc : centreUtc;
		var operatorMarkers = targets.OperatorMarkers;

		return new NavigationSteps(
			RunWindow(MarkerNavigation.PreviousRun(targets.Runs, referenceUtc), firstSampleUtc),
			RunWindow(MarkerNavigation.NextRun(targets.Runs, referenceUtc), firstSampleUtc),
			RunWindow(MarkerNavigation.LatestRun(targets.Runs), firstSampleUtc),
			MarkerWindow(MarkerNavigation.PreviousMarker(operatorMarkers, referenceUtc), window, firstSampleUtc),
			MarkerWindow(MarkerNavigation.NextMarker(operatorMarkers, referenceUtc), window, firstSampleUtc));
	}

	public static DateTime CentreOf((DateTime FromUtc, DateTime ToUtc) window)
	{
		return window.FromUtc + HalfOf(window);
	}

	/// <summary>The window of the same width whose centre is the time.</summary>
	public static (DateTime FromUtc, DateTime ToUtc) Centred(
		DateTime timeUtc,
		(DateTime FromUtc, DateTime ToUtc) window)
	{
		var fromUtc = timeUtc - HalfOf(window);

		return (fromUtc, fromUtc + (window.ToUtc - window.FromUtc));
	}

	/// <summary>Half the width in whole ticks, so a centred time reads back as the centre exactly.</summary>
	private static TimeSpan HalfOf((DateTime FromUtc, DateTime ToUtc) window)
	{
		return TimeSpan.FromTicks((window.ToUtc - window.FromUtc).Ticks / 2);
	}

	/// <summary>No target without a sample in the run; one starting before the floor is framed from it.</summary>
	private static (DateTime FromUtc, DateTime ToUtc)? RunWindow(RecipeRun? run, DateTime firstSampleUtc)
	{
		if (run is not { } target || target.EndUtc <= firstSampleUtc)
		{
			return null;
		}

		var sampled = target.StartUtc < firstSampleUtc ? target with { StartUtc = firstSampleUtc } : target;

		return MarkerNavigation.Frame(sampled);
	}

	/// <summary>No target before the first sample, or where the floor clamps the window back onto itself.</summary>
	private static (DateTime FromUtc, DateTime ToUtc)? MarkerWindow(
		StoredMarker? marker,
		(DateTime FromUtc, DateTime ToUtc) window,
		DateTime firstSampleUtc)
	{
		if (marker is null || marker.TimeUtc < firstSampleUtc)
		{
			return null;
		}

		var centred = Centred(marker.TimeUtc, window);
		var placedFromUtc = centred.FromUtc < firstSampleUtc ? firstSampleUtc : centred.FromUtc;

		return placedFromUtc == window.FromUtc ? null : centred;
	}
}
