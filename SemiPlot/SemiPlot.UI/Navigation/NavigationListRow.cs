using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Navigation;

/// <summary>
/// One entry of a navigation list: an operator marker, or a recipe run with its duration in whole minutes; the key
/// is the marker's id or the run's start in ticks.
/// </summary>
public sealed record NavigationListEntry(
	long Key,
	DateTime TimeUtc,
	string? Name,
	TimeSpan? Duration = null,
	bool IsInterrupted = false)
{
	public static NavigationListEntry Of(StoredMarker marker)
	{
		var name = string.IsNullOrWhiteSpace(marker.Label) ? null : marker.Label;

		return new NavigationListEntry(marker.Id, marker.TimeUtc, name);
	}

	public static NavigationListEntry Of(RecipeRun run)
	{
		var minutes = Math.Round((run.EndUtc - run.StartUtc).TotalMinutes, MidpointRounding.AwayFromZero);

		return new NavigationListEntry(
			run.StartUtc.Ticks, run.StartUtc, run.Label, TimeSpan.FromMinutes(minutes), run.IsInterrupted);
	}

	/// <summary>How far the time lies from the entry: from its moment, or from its run, zero inside it.</summary>
	public TimeSpan DistanceTo(DateTime timeUtc)
	{
		var endUtc = TimeUtc + (Duration ?? TimeSpan.Zero);
		if (timeUtc < TimeUtc)
		{
			return TimeUtc - timeUtc;
		}

		return timeUtc > endUtc ? timeUtc - endUtc : TimeSpan.Zero;
	}
}

/// <summary>One row of a navigation list: a month header or an entry.</summary>
public abstract class NavigationListRow : ReactiveObject
{
}

/// <summary>A local month in capitals and the count of its entries the filter keeps.</summary>
public sealed class NavigationListHeader(string text, int count) : NavigationListRow
{
	public string Text { get; } = text;

	public int Count { get; } = count;
}

/// <summary>An entry's name, local date and time and, for a run, its duration; the list marks one row active.</summary>
public sealed class NavigationListItem(NavigationListEntry entry, string dateText, string? durationText)
	: NavigationListRow
{
	public NavigationListEntry Entry { get; } = entry;

	/// <summary>The marker's label or the run's recipe, empty for an entry with none.</summary>
	public string Name { get; } = entry.Name ?? string.Empty;

	/// <summary>The full name for the row's tooltip; null for an entry with none.</summary>
	public string? NameTip => Name.Length > 0 ? Name : null;

	public string DateText { get; } = dateText;

	/// <summary>The run's duration; null for a marker, whose row shows no flag and no duration column.</summary>
	public string? DurationText { get; } = durationText;

	public bool IsRun => DurationText is not null;

	public bool IsInterrupted => Entry.IsInterrupted;

	/// <summary>True for the one row Enter goes to; <see cref="NavigationListViewModel"/> writes it.</summary>
	public bool IsActive
	{
		get;
		internal set => this.RaiseAndSetIfChanged(ref field, value);
	}
}
