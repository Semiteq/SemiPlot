using SemiPlot.Core.Data;

namespace SemiPlot.Core.Trends;

/// <summary>
/// A recipe run from its start marker to its closing marker, the next start or the newest sample; the label is the
/// start marker's, the recipe the run names.
/// </summary>
public readonly record struct RecipeRun(DateTime StartUtc, DateTime EndUtc, bool IsInterrupted, string? Label = null);

/// <summary>
/// Recipe runs paired from the stored markers, and the steps between runs and between operator markers. Every
/// function reads its markers in time order, then by id, as <see cref="MarkerSet.All"/> holds them; the marker
/// steps take <see cref="MarkerSet.OperatorMarkers"/>.
/// </summary>
public static class MarkerNavigation
{
	private const int FrameMarginDivisor = 10;

	/// <summary>
	/// The runs in time order. A start without a close runs to the next start, the last one to the newest sample; a
	/// close without a start and a run of no length are skipped.
	/// </summary>
	public static IReadOnlyList<RecipeRun> Runs(IReadOnlyList<StoredMarker> markers, DateTime? newestSampleUtc)
	{
		var runs = new List<RecipeRun>();
		StoredMarker? openStart = null;
		foreach (var marker in markers)
		{
			if (marker.Kind == MarkerKind.RecipeStart)
			{
				if (openStart is not null)
				{
					AddRun(runs, openStart, marker.TimeUtc, isInterrupted: false);
				}

				openStart = marker;
			}
			else if (marker.Kind is MarkerKind.RecipeEnd or MarkerKind.RecipeInterrupted && openStart is not null)
			{
				AddRun(runs, openStart, marker.TimeUtc, marker.Kind == MarkerKind.RecipeInterrupted);
				openStart = null;
			}
		}

		if (openStart is not null && newestSampleUtc is { } newestUtc)
		{
			AddRun(runs, openStart, newestUtc, isInterrupted: false);
		}

		return runs;
	}

	/// <summary>The first run that starts after the time, or null.</summary>
	public static RecipeRun? NextRun(IReadOnlyList<RecipeRun> runs, DateTime timeUtc)
	{
		var index = SortedSearch.FirstIndex(runs.Count, position => runs[position].StartUtc > timeUtc);

		return index < runs.Count ? runs[index] : null;
	}

	/// <summary>The last run that ends before the time, or null.</summary>
	public static RecipeRun? PreviousRun(IReadOnlyList<RecipeRun> runs, DateTime timeUtc)
	{
		var index = SortedSearch.FirstIndex(runs.Count, position => runs[position].EndUtc >= timeUtc) - 1;

		return index >= 0 ? runs[index] : null;
	}

	public static RecipeRun? LatestRun(IReadOnlyList<RecipeRun> runs)
	{
		return runs.Count > 0 ? runs[^1] : null;
	}

	/// <summary>The run plus a tenth of its length on each side.</summary>
	public static (DateTime FromUtc, DateTime ToUtc) Frame(RecipeRun run)
	{
		var margin = TimeSpan.FromTicks((run.EndUtc - run.StartUtc).Ticks / FrameMarginDivisor);

		return (run.StartUtc - margin, run.EndUtc + margin);
	}

	public static StoredMarker? NextMarker(IReadOnlyList<StoredMarker> markers, DateTime timeUtc)
	{
		var index = SortedSearch.FirstIndex(markers.Count, position => markers[position].TimeUtc > timeUtc);

		return index < markers.Count ? markers[index] : null;
	}

	public static StoredMarker? PreviousMarker(IReadOnlyList<StoredMarker> markers, DateTime timeUtc)
	{
		var index = SortedSearch.FirstIndex(markers.Count, position => markers[position].TimeUtc >= timeUtc) - 1;

		return index >= 0 ? markers[index] : null;
	}

	private static void AddRun(List<RecipeRun> runs, StoredMarker start, DateTime endUtc, bool isInterrupted)
	{
		if (endUtc > start.TimeUtc)
		{
			var label = string.IsNullOrWhiteSpace(start.Label) ? null : start.Label;
			runs.Add(new RecipeRun(start.TimeUtc, endUtc, isInterrupted, label));
		}
	}
}
