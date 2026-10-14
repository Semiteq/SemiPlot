using AwesomeAssertions;

using SemiPlot.UI.Export;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Export;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ExportRangeFormTests
{
	private static readonly TimeZoneInfo _plusThree =
		TimeZoneInfo.CreateCustomTimeZone("PlusThree", TimeSpan.FromHours(3), "PlusThree", "PlusThree");

	private static readonly DateTime _startUtc = new(2026, 10, 13, 5, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _day = new(2026, 10, 13);

	private static ExportRangeForm FormWith(
		string startTime, string endTime, int tickedPens = 1, int endDayOffset = 0)
	{
		return new ExportRangeForm(_startUtc, _startUtc.AddHours(1), _plusThree)
		{
			StartDate = _day,
			StartTimeText = startTime,
			EndDate = _day.AddDays(endDayOffset),
			EndTimeText = endTime,
			TickedPenCount = tickedPens
		};
	}

	[Fact]
	public void Prefill_FloorsTheStartAndCeilsTheEndInWallClock()
	{
		var form = new ExportRangeForm(
			_startUtc.AddMilliseconds(900), _startUtc.AddHours(1).AddMilliseconds(100), _plusThree);

		form.StartDate.Should().Be(_day);
		form.StartTimeText.Should().Be("08:00:00");
		form.EndDate.Should().Be(_day);
		form.EndTimeText.Should().Be("09:00:01");
	}

	[Fact]
	public void Prefill_KeepsAWholeSecondEdgeAsItIs()
	{
		var form = new ExportRangeForm(_startUtc, _startUtc.AddHours(12), _plusThree);

		form.StartTimeText.Should().Be("08:00:00");
		form.EndTimeText.Should().Be("20:00:00");
	}

	[Fact]
	public void Prefill_MovesAnEndCeiledAcrossMidnightOntoTheNextDate()
	{
		var beforeMidnightUtc = new DateTime(2026, 10, 13, 20, 59, 59, 500, DateTimeKind.Utc);

		var form = new ExportRangeForm(beforeMidnightUtc.AddHours(-1), beforeMidnightUtc, _plusThree);

		form.EndDate.Should().Be(_day.AddDays(1));
		form.EndTimeText.Should().Be("00:00:00");
		form.StartDate.Should().Be(_day);
		form.StartTimeText.Should().Be("22:59:59");
	}

	[Fact]
	public void ATwelveHourRange_IsValid()
	{
		var form = FormWith("08:00:00", "20:00:00");

		form.Problem.Should().Be(ExportRangeProblem.None);
		form.IsValid.Should().BeTrue();
	}

	[Fact]
	public void ExactlyTwentyFourHours_IsValid()
	{
		var form = FormWith("08:00:00", "08:00:00", endDayOffset: 1);

		form.IsValid.Should().BeTrue();
	}

	[Fact]
	public void TwentyFourHoursPlusOneSecond_MarksBothEndControlsWithTheSpanProblem()
	{
		var form = FormWith("08:00:00", "08:00:01", endDayOffset: 1);

		form.Problem.Should().Be(ExportRangeProblem.SpanOverLimit);
		form.IsEndDateInvalid.Should().BeTrue();
		form.IsEndTimeInvalid.Should().BeTrue();
		form.IsStartDateInvalid.Should().BeFalse();
		form.IsStartTimeInvalid.Should().BeFalse();
	}

	[Theory]
	[InlineData("09:00:00")]
	[InlineData("08:00:00")]
	public void AnEndNotAfterTheStart_MarksBothEndControls(string endTime)
	{
		var form = FormWith("09:00:00", endTime);

		form.Problem.Should().Be(ExportRangeProblem.EndNotAfterStart);
		form.IsEndDateInvalid.Should().BeTrue();
		form.IsEndTimeInvalid.Should().BeTrue();
		form.IsStartDateInvalid.Should().BeFalse();
		form.IsStartTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void AMissingStartDate_MarksTheStartDateOnly()
	{
		var form = FormWith("08:00:00", "09:00:00");

		form.StartDate = null;

		form.Problem.Should().Be(ExportRangeProblem.StartDateMissing);
		form.IsStartDateInvalid.Should().BeTrue();
		form.IsStartTimeInvalid.Should().BeFalse();
		form.IsEndDateInvalid.Should().BeFalse();
		form.IsEndTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void AMissingEndDate_MarksTheEndDateOnly()
	{
		var form = FormWith("08:00:00", "09:00:00");

		form.EndDate = null;

		form.Problem.Should().Be(ExportRangeProblem.EndDateMissing);
		form.IsEndDateInvalid.Should().BeTrue();
		form.IsEndTimeInvalid.Should().BeFalse();
		form.IsStartDateInvalid.Should().BeFalse();
		form.IsStartTimeInvalid.Should().BeFalse();
	}

	[Theory]
	[InlineData("25:00:00")]
	[InlineData("8:00")]
	[InlineData("08:00:00 13.10.2026")]
	[InlineData("")]
	public void AnUnreadableStartTime_MarksTheStartTimeBox(string startTime)
	{
		var form = FormWith(startTime, "09:00:00");

		form.Problem.Should().Be(ExportRangeProblem.StartUnreadable);
		form.IsStartTimeInvalid.Should().BeTrue();
		form.IsStartDateInvalid.Should().BeFalse();
		form.IsEndTimeInvalid.Should().BeFalse();
	}

	[Theory]
	[InlineData("25:00:00")]
	[InlineData("8:00")]
	[InlineData("08:00:00 13.10.2026")]
	[InlineData("")]
	public void AnUnreadableEndTime_MarksTheEndTimeBox(string endTime)
	{
		var form = FormWith("08:00:00", endTime);

		form.Problem.Should().Be(ExportRangeProblem.EndUnreadable);
		form.IsEndTimeInvalid.Should().BeTrue();
		form.IsEndDateInvalid.Should().BeFalse();
		form.IsStartTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void ATimeWithSurroundingBlanks_IsRead()
	{
		var form = FormWith(" 08:00:00 ", "09:00:00  ");

		form.IsValid.Should().BeTrue();
	}

	[Fact]
	public void Messages_ComeInFormOrderAcrossAllSevenArms()
	{
		var form = FormWith("junk", "junk", tickedPens: 0);
		form.StartDate = null;
		form.EndDate = null;
		form.Problem.Should().Be(ExportRangeProblem.StartDateMissing);

		form.StartDate = _day;
		form.Problem.Should().Be(ExportRangeProblem.StartUnreadable);

		form.StartTimeText = "08:00:00";
		form.Problem.Should().Be(ExportRangeProblem.EndDateMissing);

		form.EndDate = _day;
		form.Problem.Should().Be(ExportRangeProblem.EndUnreadable);

		form.EndTimeText = "07:00:00";
		form.Problem.Should().Be(ExportRangeProblem.EndNotAfterStart);

		form.EndDate = _day.AddDays(2);
		form.Problem.Should().Be(ExportRangeProblem.SpanOverLimit);

		form.EndDate = _day;
		form.EndTimeText = "09:00:00";
		form.Problem.Should().Be(ExportRangeProblem.NoPenTicked);

		form.TickedPenCount = 1;
		form.Problem.Should().Be(ExportRangeProblem.None);
	}

	[Fact]
	public void NoPenTicked_MarksNoControl()
	{
		var form = FormWith("08:00:00", "09:00:00", tickedPens: 0);

		form.IsValid.Should().BeFalse();
		form.IsStartDateInvalid.Should().BeFalse();
		form.IsStartTimeInvalid.Should().BeFalse();
		form.IsEndDateInvalid.Should().BeFalse();
		form.IsEndTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void TheParsedValues_CarryNoConversion()
	{
		var form = FormWith("08:00:00", "09:30:15");

		form.From.Should().Be(new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Unspecified));
		form.To.Should().Be(new DateTime(2026, 10, 13, 9, 30, 15, DateTimeKind.Unspecified));
		form.From!.Value.Kind.Should().Be(DateTimeKind.Unspecified);
	}

	[Fact]
	public void ADateWithATimeOfDay_ContributesOnlyItsDay()
	{
		var form = FormWith("08:00:00", "09:00:00");

		form.StartDate = new DateTime(2026, 10, 13, 17, 45, 0);

		form.From.Should().Be(new DateTime(2026, 10, 13, 8, 0, 0));
	}

	[Fact]
	public void TheParsedValues_AreAbsentWhileTheFormIsInvalid()
	{
		var form = FormWith("junk", "09:00:00");

		form.From.Should().BeNull();
		form.To.Should().BeNull();
	}

	[Fact]
	public void AnEdit_RaisesTheDerivedProperties()
	{
		var form = FormWith("08:00:00", "09:00:00");
		var raised = new List<string?>();
		form.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

		form.EndTimeText = "junk";

		raised.Should().Contain(
			[
				nameof(ExportRangeForm.IsValid),
				nameof(ExportRangeForm.IsEndTimeInvalid),
				nameof(ExportRangeForm.Problem)
			]);

		raised.Clear();
		form.StartDate = null;

		raised.Should().Contain(
			[
				nameof(ExportRangeForm.IsValid),
				nameof(ExportRangeForm.IsStartDateInvalid),
				nameof(ExportRangeForm.From)
			]);
	}
}
