using System.Collections.Specialized;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.UI.Export;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Export;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ExportMarkerChoiceTests : IDisposable
{
	private static readonly DateTime _day = new(2026, 10, 13);

	private static readonly StoredMarker _userAtTen = Marker(1, _day.AddHours(10), "Note");
	private static readonly StoredMarker _recipeStartAtNine =
		Marker(2, _day.AddHours(9), "Etch.csv", MarkerKind.RecipeStart);
	private static readonly StoredMarker _recipeEndAtHalfPastNine =
		Marker(3, _day.AddHours(9.5), "Etch.csv", MarkerKind.RecipeEnd);
	private static readonly StoredMarker _interruptedAtEleven =
		Marker(4, _day.AddHours(11), "Etch.csv", MarkerKind.RecipeInterrupted);
	private static readonly StoredMarker _recipeEndAtSeven = Marker(5, _day.AddHours(7), null, MarkerKind.RecipeEnd);
	private static readonly StoredMarker _recipeStartNextDay =
		Marker(6, _day.AddDays(1).AddHours(9), "Etch.csv", MarkerKind.RecipeStart);

	private static readonly StoredMarker[] _markers =
	[
		_userAtTen, _recipeStartAtNine, _recipeEndAtHalfPastNine, _interruptedAtEleven, _recipeEndAtSeven,
		_recipeStartNextDay
	];

	private readonly TestScheduler _scheduler = new();
	private readonly MessagePanelViewModel _messagePanel = new();
	private CsvExportViewModel? _viewModel;

	public void Dispose()
	{
		_viewModel?.Dispose();
		_messagePanel.Dispose();
	}

	private static StoredMarker Marker(long id, DateTime wallClock, string? label, MarkerKind kind = MarkerKind.User)
	{
		return new StoredMarker(id, CsvExportStand.ToUtc(wallClock), label, kind);
	}

	private static IReadOnlyList<StoredMarker?> StartList(DateTime? date, params StoredMarker[] markers)
	{
		return
		[
			.. ExportMarkerChoice.ForStart(markers, CsvExportStand.PlusThree, date)
				.Select(choice => choice.Marker)
		];
	}

	private static IReadOnlyList<StoredMarker?> EndList(
		DateTime? date, DateTime? startBound, params StoredMarker[] markers)
	{
		return
		[
			.. ExportMarkerChoice.ForEnd(markers, CsvExportStand.PlusThree, date, startBound)
				.Select(choice => choice.Marker)
		];
	}

	private CsvExportViewModel BuildViewModel(params StoredMarker[] markers)
	{
		_viewModel = CsvExportStand.Build(
			new FakeArchiveRowReader(),
			_messagePanel,
			_scheduler,
			[new ExportPenChoice(CsvExportStand.Pressure, true)],
			markers: markers);

		return _viewModel;
	}

	private static ExportMarkerChoice Only(IReadOnlyList<ExportMarkerChoice> choices, StoredMarker marker)
	{
		return choices.Should().ContainSingle(choice => choice.Marker == marker).Subject;
	}

	[Fact]
	public void TheStartList_HoldsUserAndRecipeStartMarkersOfTheDate_InTimeOrder()
	{
		StartList(_day, _markers).Should().Equal(_recipeStartAtNine, _userAtTen);
	}

	[Fact]
	public void TheEndList_HoldsUserRecipeEndAndInterruptedMarkersPastTheStart_InTimeOrder()
	{
		EndList(_day, _day.AddHours(8), _markers)
			.Should().Equal(_recipeEndAtHalfPastNine, _userAtTen, _interruptedAtEleven);
	}

	[Fact]
	public void WithoutAReadableStart_TheEndListHoldsEveryEndKindMarkerOfTheDate()
	{
		EndList(_day, null, _markers)
			.Should().Equal(_recipeEndAtSeven, _recipeEndAtHalfPastNine, _userAtTen, _interruptedAtEleven);
	}

	[Fact]
	public void AnEndMarkerInTheStartsSecondOrBefore_IsNotListed_AndOneInTheNextSecondIs()
	{
		var start = _day.AddHours(9);
		var halfSecondBefore = Marker(10, start.AddMilliseconds(-500), null, MarkerKind.RecipeEnd);
		var onTheStart = Marker(11, start, null, MarkerKind.RecipeEnd);
		var lateInTheStartsSecond = Marker(12, start.AddMilliseconds(900), null, MarkerKind.RecipeEnd);
		var nextSecond = Marker(13, start.AddSeconds(1), null, MarkerKind.RecipeEnd);

		EndList(_day, start, halfSecondBefore, onTheStart, lateInTheStartsSecond, nextSecond)
			.Should().Equal(nextSecond);
	}

	[Fact]
	public void TheDay_IsTheMarkersWallClockDayInTheArchiveZone()
	{
		var justAfterMidnight = Marker(20, _day.AddMinutes(30), null);
		var justBeforeMidnight = Marker(21, _day.AddMinutes(-30), null);

		justAfterMidnight.TimeUtc.Date.Should().Be(_day.AddDays(-1), "the UTC moment falls on the previous date");
		StartList(_day, justAfterMidnight, justBeforeMidnight).Should().Equal(justAfterMidnight);
		StartList(_day.AddDays(-1), justAfterMidnight, justBeforeMidnight).Should().Equal(justBeforeMidnight);
	}

	[Fact]
	public void InTheRepeatedAutumnHour_AnEndMarkerOfTheSecondPassEarlierOnTheWallClock_IsNotListed()
	{
		// Europe/Berlin falls back at 01:00 UTC on 2026-10-25: 02:00-03:00 runs once at +02:00, then at +01:00.
		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		var fallBackDay = new DateTime(2026, 10, 25);
		var firstPassStart = fallBackDay.AddHours(2.5);
		var secondPassAtQuarterPast = new StoredMarker(
			40, new DateTime(2026, 10, 25, 1, 15, 0, DateTimeKind.Utc), null, MarkerKind.RecipeEnd);
		var secondPassAtQuarterTo = new StoredMarker(
			41, new DateTime(2026, 10, 25, 1, 45, 0, DateTimeKind.Utc), null, MarkerKind.RecipeEnd);

		var listed = ExportMarkerChoice.ForEnd(
			[secondPassAtQuarterPast, secondPassAtQuarterTo], berlin, fallBackDay, firstPassStart);

		listed.Select(choice => choice.Marker).Should().Equal(secondPassAtQuarterTo);
		listed.Single().WallClock.Should().Be(fallBackDay.AddHours(2.75));
	}

	[Fact]
	public void WithoutADate_BothListsAreEmpty()
	{
		StartList(null, _markers).Should().BeEmpty();
		EndList(null, null, _markers).Should().BeEmpty();
	}

	[Fact]
	public void AnItem_ReadsTheTimeAndTheStoredLabel_WithNoKindWord()
	{
		var choices = ExportMarkerChoice.ForEnd(_markers, CsvExportStand.PlusThree, _day, null);

		Only(choices, _interruptedAtEleven).Text.Should().Be("11:00:00 Etch.csv");
		Only(choices, _userAtTen).Text.Should().Be("10:00:00 Note");
		Only(ExportMarkerChoice.ForStart(_markers, CsvExportStand.PlusThree, _day), _recipeStartAtNine)
			.Text.Should().Be("09:00:00 Etch.csv");
	}

	[Theory]
	[InlineData("ru", "Etch.csv", "Etch.csv")]
	[InlineData("en", "Etch.csv", "Etch.csv")]
	[InlineData("ru", null, null)]
	[InlineData("en", " ", null)]
	public void TheLabel_IsTheStoredLabelAlone_OrUnnamed(string culture, string? stored, string? expected)
	{
		using var cultureScope = new CultureScope(culture);
		var marker = Marker(8, _day.AddHours(7), stored, MarkerKind.RecipeEnd);

		ExportMarkerChoice.ForEnd([marker], CsvExportStand.PlusThree, _day, null).Single().Label
			.Should().Be(expected ?? Resources.ExportMarkerUnnamed);
		ExportMarkerChoice.NoMarker().Label.Should().Be(Resources.ExportNoMarker);
	}

	[Theory]
	[InlineData("ru", null)]
	[InlineData("en", null)]
	[InlineData("ru", "   ")]
	[InlineData("en", "")]
	public void AnUnlabelledMarker_ReadsTheTimeAndUnnamed(string culture, string? label)
	{
		using var cultureScope = new CultureScope(culture);
		var marker = Marker(7, _day.AddHours(7), label, MarkerKind.RecipeEnd);

		ExportMarkerChoice.ForEnd([marker], CsvExportStand.PlusThree, _day, null).Single().Text
			.Should().Be($"07:00:00 {Resources.ExportMarkerUnnamed}");
	}

	[AvaloniaTheory]
	[InlineData("ru")]
	[InlineData("en")]
	public void EachBox_LeadsWithNoMarker_AndStartsOnIt(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		var viewModel = BuildViewModel(_markers);

		viewModel.StartChoices.Select(choice => choice.Text)
			.Should().Equal(Resources.ExportNoMarker, "09:00:00 Etch.csv", "10:00:00 Note");
		viewModel.EndChoices[0].IsNoMarker.Should().BeTrue();
		viewModel.EndChoices.Skip(1).Should().Equal(viewModel.EndMarkers);
		viewModel.SelectedStartMarker.Should().Be(viewModel.StartChoices[0]);
		viewModel.SelectedEndMarker.Should().Be(viewModel.EndChoices[0]);
	}

	[AvaloniaFact]
	public void ChoosingAStartMarker_WritesItsFlooredSecondAndItsDate()
	{
		var marker = Marker(30, _day.AddDays(-1).AddHours(14).AddMinutes(3).AddMilliseconds(700), "Etch.csv",
			MarkerKind.RecipeStart);
		var viewModel = BuildViewModel(marker);

		viewModel.Form.StartDate = _day.AddDays(-1);
		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, marker);

		viewModel.Form.StartDate.Should().Be(_day.AddDays(-1));
		viewModel.Form.StartTimeText.Should().Be("14:03:00");
		viewModel.SelectedStartMarker.Marker.Should().Be(marker);
	}

	[AvaloniaFact]
	public void ChoosingAnEndMarkerOnAWholeSecond_WritesTheNextSecond()
	{
		var viewModel = BuildViewModel(_markers);

		viewModel.SelectedEndMarker = Only(viewModel.EndMarkers, _recipeEndAtHalfPastNine);

		viewModel.Form.EndDate.Should().Be(_day);
		viewModel.Form.EndTimeText.Should().Be("09:30:01");
		viewModel.Form.From.Should().Be(_day.AddHours(8));
		viewModel.Form.To.Should().Be(_day.AddHours(9.5).AddSeconds(1));
		viewModel.SelectedEndMarker.Marker.Should().Be(_recipeEndAtHalfPastNine);
	}

	[AvaloniaFact]
	public void ChoosingAStartMarkerOfAnotherDate_WritesTheDateAndTheTimeAsOneEdit_AndKeepsTheChoice()
	{
		var dayBefore = Marker(33, _day.AddDays(-1).AddHours(23), "Etch.csv", MarkerKind.RecipeStart);
		var viewModel = BuildViewModel(dayBefore);
		viewModel.Form.StartDate = _day.AddDays(-1);
		var choice = Only(viewModel.StartMarkers, dayBefore);
		viewModel.Form.StartDate = _day;

		viewModel.SelectedStartMarker = choice;

		viewModel.Form.StartDate.Should().Be(_day.AddDays(-1));
		viewModel.Form.StartTimeText.Should().Be("23:00:00");
		viewModel.SelectedStartMarker.Should().Be(choice, "no half-written start releases the choice");
	}

	[AvaloniaFact]
	public void ChoosingAnEndMarkerInTheDaysLastSecond_MovesTheEndOntoTheNextDate_AndReleasesTheBox()
	{
		var marker = Marker(31, _day.AddDays(1).AddMilliseconds(-500), null, MarkerKind.RecipeEnd);
		var viewModel = BuildViewModel(marker);

		var choice = Only(viewModel.EndMarkers, marker);

		viewModel.SelectedEndMarker = choice;

		viewModel.SelectedEndMarker.Should().Be(choice, "the edge moves after the box's pick returns");
		_scheduler.AdvanceBy(1);
		viewModel.Form.EndDate.Should().Be(_day.AddDays(1));
		viewModel.Form.EndTimeText.Should().Be("00:00:00");
		viewModel.SelectedEndMarker.IsNoMarker.Should().BeTrue("the next date's list does not hold the marker");
	}

	[AvaloniaFact]
	public void AHandEditOfTheTime_ReleasesThatEdgesChoiceOnly()
	{
		var viewModel = BuildViewModel(_markers);
		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _recipeStartAtNine);
		viewModel.SelectedEndMarker = Only(viewModel.EndMarkers, _interruptedAtEleven);

		viewModel.Form.StartTimeText = "09:00:01";

		viewModel.SelectedStartMarker.IsNoMarker.Should().BeTrue();
		viewModel.SelectedEndMarker.Marker.Should().Be(_interruptedAtEleven);

		viewModel.Form.EndTimeText = "11:00:02";

		viewModel.SelectedEndMarker.IsNoMarker.Should().BeTrue();
		viewModel.Form.IsValid.Should().BeTrue("the edges are independent");
	}

	[AvaloniaFact]
	public void ADateChange_ReleasesThatEdgesChoice_AndLeavesItsTime()
	{
		var viewModel = BuildViewModel(_markers);
		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _recipeStartAtNine);
		viewModel.SelectedEndMarker = Only(viewModel.EndMarkers, _interruptedAtEleven);

		viewModel.Form.EndDate = _day.AddDays(1);

		viewModel.SelectedEndMarker.IsNoMarker.Should().BeTrue();
		viewModel.SelectedStartMarker.Marker.Should().Be(_recipeStartAtNine);

		viewModel.Form.StartDate = _day.AddDays(-1);

		viewModel.SelectedStartMarker.IsNoMarker.Should().BeTrue();
		viewModel.Form.StartTimeText.Should().Be("09:00:00");
	}

	[AvaloniaFact]
	public void AStartMovedPastTheChosenEnd_ReleasesTheEndChoice()
	{
		var viewModel = BuildViewModel(_markers);
		viewModel.SelectedEndMarker = Only(viewModel.EndMarkers, _recipeEndAtHalfPastNine);

		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _userAtTen);

		viewModel.SelectedEndMarker.IsNoMarker.Should().BeTrue("the end list past 10:00 does not hold 09:30");
		viewModel.Form.EndTimeText.Should().Be("09:30:01");
	}

	[AvaloniaFact]
	public void AUserMarkerChosenAsTheStart_IsAbsentFromTheEndList()
	{
		var viewModel = BuildViewModel(_markers);
		viewModel.EndMarkers.Select(choice => choice.Marker).Should().Contain(_userAtTen);

		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _userAtTen);

		viewModel.EndMarkers.Select(choice => choice.Marker).Should().Equal(_interruptedAtEleven);
	}

	[AvaloniaTheory]
	[InlineData(false)]
	[InlineData(true)]
	public void ANullFromABox_KeepsItsChoice(bool endEdge)
	{
		var viewModel = BuildViewModel(_markers);
		var start = Only(viewModel.StartMarkers, _recipeStartAtNine);
		var end = Only(viewModel.EndMarkers, _interruptedAtEleven);
		viewModel.SelectedStartMarker = start;
		viewModel.SelectedEndMarker = end;
		var raised = new List<string?>();
		viewModel.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

		if (endEdge)
		{
			viewModel.SelectedEndMarker = null;
		}
		else
		{
			viewModel.SelectedStartMarker = null;
		}

		raised.Should().BeEmpty("only a stale edge releases a choice");
		viewModel.SelectedStartMarker.Should().Be(start);
		viewModel.SelectedEndMarker.Should().Be(end);
	}

	[AvaloniaFact]
	public void AnEndMarkerHeld_StaysChosen_WhenAStartMarkerIsChosen()
	{
		var viewModel = BuildViewModel(_markers);
		var end = Only(viewModel.EndMarkers, _interruptedAtEleven);
		viewModel.SelectedEndMarker = end;

		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _recipeStartAtNine);

		viewModel.SelectedEndMarker.Should().Be(end);
		viewModel.EndChoices.Should().Contain(end);
		viewModel.Form.EndTimeText.Should().Be("11:00:01");
	}

	[AvaloniaFact]
	public void TheChoiceLists_AreOneCollectionEach_UpdatedInPlaceWithoutAReset()
	{
		var viewModel = BuildViewModel(_markers);
		var startChoices = viewModel.StartChoices;
		var endChoices = viewModel.EndChoices;
		var noMarker = startChoices[0];
		var actions = new List<NotifyCollectionChangedAction>();
		((INotifyCollectionChanged)startChoices).CollectionChanged += (_, change) => actions.Add(change.Action);
		((INotifyCollectionChanged)endChoices).CollectionChanged += (_, change) => actions.Add(change.Action);

		foreach (var edit in new Action[]
			{
				() => viewModel.Form.StartDate = _day.AddDays(1),
				() => viewModel.Form.StartDate = _day,
				() => viewModel.Form.StartTimeText = "09:45:00",
				() => viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _recipeStartAtNine),
				() => viewModel.Form.EndDate = _day.AddDays(-1)
			})
		{
			edit();

			viewModel.StartChoices.Should().BeSameAs(startChoices);
			viewModel.EndChoices.Should().BeSameAs(endChoices);
			startChoices[0].Should().BeSameAs(noMarker);
			endChoices[0].Should().BeSameAs(noMarker);
			startChoices.Skip(1).Should().Equal(viewModel.StartMarkers);
			endChoices.Skip(1).Should().Equal(viewModel.EndMarkers);
		}

		actions.Should().NotBeEmpty().And.OnlyContain(
			action => action == NotifyCollectionChangedAction.Add || action == NotifyCollectionChangedAction.Remove);
	}

	[AvaloniaFact]
	public void ChoosingNoMarker_LeavesTheTime()
	{
		var viewModel = BuildViewModel(_markers);
		viewModel.SelectedStartMarker = Only(viewModel.StartMarkers, _recipeStartAtNine);

		viewModel.SelectedStartMarker = viewModel.StartChoices[0];

		viewModel.Form.StartTimeText.Should().Be("09:00:00");
		viewModel.SelectedStartMarker.IsNoMarker.Should().BeTrue();
	}

	[AvaloniaFact]
	public void EditingTheStartDate_RefreshesBothLists()
	{
		var dayBefore = Marker(32, _day.AddDays(-1).AddHours(9), "Etch.csv", MarkerKind.RecipeStart);
		var viewModel = BuildViewModel([.. _markers, dayBefore]);

		viewModel.StartMarkers.Select(choice => choice.Marker).Should().Equal(_recipeStartAtNine, _userAtTen);
		viewModel.EndMarkers.Select(choice => choice.Marker)
			.Should().Equal(_recipeEndAtHalfPastNine, _userAtTen, _interruptedAtEleven);

		viewModel.Form.StartDate = _day.AddDays(-1);

		viewModel.StartMarkers.Select(choice => choice.Marker).Should().Equal(dayBefore);
		viewModel.EndMarkers.Select(choice => choice.Marker)
			.Should().Equal(_recipeEndAtSeven, _recipeEndAtHalfPastNine, _userAtTen, _interruptedAtEleven);
	}

	[AvaloniaFact]
	public void AnEndNotAfterTheStart_StillFiltersTheEndListByTheTypedStart()
	{
		var viewModel = BuildViewModel(_markers);

		viewModel.Form.StartTimeText = "09:45:00";
		viewModel.Form.EndTimeText = "07:00:00";

		viewModel.Form.Problem.Should().Be(ExportRangeProblem.EndNotAfterStart);
		viewModel.Form.From.Should().BeNull();
		viewModel.EndMarkers.Select(choice => choice.Marker).Should().Equal(_userAtTen, _interruptedAtEleven);
	}

	[AvaloniaFact]
	public void AnUnreadableStart_ListsEveryEndKindMarkerOfTheEndDate()
	{
		var viewModel = BuildViewModel(_markers);

		viewModel.Form.StartTimeText = "9:45";

		viewModel.EndMarkers.Select(choice => choice.Marker)
			.Should().Equal(_recipeEndAtSeven, _recipeEndAtHalfPastNine, _userAtTen, _interruptedAtEleven);
	}
}
