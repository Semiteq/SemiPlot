using System.Reactive.Subjects;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationListViewModelTests : IDisposable
{
	private static readonly DateTime _noon = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

	private readonly CultureScope _culture = new("en-US");
	private readonly BehaviorSubject<IReadOnlyList<NavigationListEntry>> _entries = new(
	[
		Marker(4, _noon.AddHours(3), "Etch end"),
		Marker(3, _noon.AddHours(1), "Etch start"),
		Marker(2, _noon.AddDays(-12), "Purge"),
		Marker(1, _noon.AddDays(-13), "Pump down")
	]);

	private readonly List<NavigationListEntry> _wentTo = [];
	private readonly NavigationListViewModel _list;
	private DateTime _centre = _noon;

	public NavigationListViewModelTests()
	{
		_list = new NavigationListViewModel(_entries, () => _centre, _wentTo.Add, TimeZoneInfo.Utc, "filter", "none");
	}

	[AvaloniaFact]
	public void Open_ClearsTheFilter_AndActivatesTheEntryNearestTheWindowCentre()
	{
		_list.FilterText = "Purge";
		_centre = _noon.AddDays(-13);

		_list.Open();

		_list.FilterText.Should().BeEmpty();
		_list.ActiveItem!.Entry.Key.Should().Be(1);
		_list.ActiveIndex.Should().Be(5, "two headers and three newer entries stand above it");
		_list.Rows.OfType<NavigationListItem>().Should().ContainSingle(item => item.IsActive);
	}

	[AvaloniaFact]
	public void MoveActive_StepsOverTheEntryRowsOnly_AndHoldsAtTheEnds()
	{
		_list.Open();
		_list.ActiveItem!.Entry.Key.Should().Be(3);

		_list.MoveActive(1);
		_list.ActiveItem!.Entry.Key.Should().Be(2, "the step skips the October header");

		_list.MoveActive(5);
		_list.ActiveItem!.Entry.Key.Should().Be(1);

		_list.MoveActive(-9);
		_list.ActiveItem!.Entry.Key.Should().Be(4);
		_list.Rows.OfType<NavigationListItem>().Count(item => item.IsActive).Should().Be(1);
	}

	[AvaloniaFact]
	public void AFilter_KeepsTheActiveEntryItKeeps_ElseActivatesTheNearestKeptOne()
	{
		_list.Open();

		_list.FilterText = "etch";
		_list.ActiveItem!.Entry.Key.Should().Be(3, "the filter keeps the active entry");

		_list.FilterText = "p";
		_list.ActiveItem!.Entry.Key.Should().Be(2, "Purge lies nearer the centre than Pump down");
		_list.HasNoMatch.Should().BeFalse();

		_list.FilterText = "zzz";
		_list.ActiveItem.Should().BeNull();
		_list.HasNoMatch.Should().BeTrue();
		_list.GoToActive().Should().BeFalse();
	}

	[AvaloniaFact]
	public void GoToActive_GoesToTheActiveEntry()
	{
		_list.Open();

		_list.GoToActive().Should().BeTrue();

		_wentTo.Select(entry => entry.Key).Should().Equal(3);
	}

	[AvaloniaFact]
	public void TheLastEntryGoneWhileOpen_ShowsTheNoMatchLine_WithAnEmptyFilter()
	{
		_list.Open();
		_list.HasNoMatch.Should().BeFalse();

		_entries.OnNext([]);

		_list.FilterText.Should().BeEmpty();
		_list.Rows.Should().BeEmpty();
		_list.ActiveItem.Should().BeNull();
		_list.HasNoMatch.Should().BeTrue("an empty frame would say nothing");
	}

	[AvaloniaFact]
	public void ARunList_ActivatesTheRunThatHoldsTheCentre_AndKeepsItWhileItsDurationGrows()
	{
		var start = _noon.AddMinutes(-30);
		_entries.OnNext(
		[
			NavigationListEntry.Of(new RecipeRun(start, _noon.AddMinutes(5), IsInterrupted: false, "Etch.csv")),
			NavigationListEntry.Of(new RecipeRun(_noon.AddHours(-3), _noon.AddHours(-2), IsInterrupted: true))
		]);
		_list.Open();
		var active = _list.ActiveItem!;

		_entries.OnNext(
		[
			NavigationListEntry.Of(new RecipeRun(start, _noon.AddMinutes(6), IsInterrupted: false, "Etch.csv")),
			NavigationListEntry.Of(new RecipeRun(_noon.AddHours(-3), _noon.AddHours(-2), IsInterrupted: true))
		]);

		active.Entry.Key.Should().Be(start.Ticks);
		_list.ActiveItem!.Entry.Key.Should().Be(start.Ticks, "a run keeps its key, its start, as it grows");
		_list.ActiveItem.DurationText.Should().Be("36 min");
		_list.ActiveItem.IsActive.Should().BeTrue();
	}

	public void Dispose()
	{
		_list.Dispose();
		_entries.Dispose();
		_culture.Dispose();
	}

	private static NavigationListEntry Marker(long id, DateTime timeUtc, string label)
	{
		return NavigationListEntry.Of(new StoredMarker(id, timeUtc, label));
	}
}
