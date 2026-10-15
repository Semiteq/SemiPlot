using AwesomeAssertions;

using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class WallClockRangeFormTests
{
	private static readonly DateTime _day = new(2026, 10, 13);

	private static RangeOnlyForm FormWith(string startTime, string endTime, int endDayOffset = 0)
	{
		return new RangeOnlyForm
		{
			StartDate = _day,
			StartTimeText = startTime,
			EndDate = _day.AddDays(endDayOffset),
			EndTimeText = endTime
		};
	}

	[Fact]
	public void Problems_ComeInFormOrder()
	{
		var form = FormWith("junk", "junk");
		form.StartDate = null;
		form.EndDate = null;
		form.Problem.Should().Be(WallClockRangeProblem.StartDateMissing);

		form.StartDate = _day;
		form.Problem.Should().Be(WallClockRangeProblem.StartUnreadable);

		form.StartTimeText = "08:00:00";
		form.Problem.Should().Be(WallClockRangeProblem.EndDateMissing);

		form.EndDate = _day;
		form.Problem.Should().Be(WallClockRangeProblem.EndUnreadable);

		form.EndTimeText = "08:00:00";
		form.Problem.Should().Be(WallClockRangeProblem.EndNotAfterStart);

		form.EndTimeText = "08:00:01";
		form.Problem.Should().Be(WallClockRangeProblem.None);
	}

	[Theory]
	[InlineData("25:00:00")]
	[InlineData("8:00")]
	[InlineData("08:00:00 13.10.2026")]
	[InlineData("")]
	public void AnUnreadableTime_IsAProblemAndGivesNoBounds(string time)
	{
		var form = FormWith(time, "09:00:00");

		form.Problem.Should().Be(WallClockRangeProblem.StartUnreadable);
		form.Bounds.Should().BeNull();
	}

	[Fact]
	public void ATimeWithSurroundingBlanks_IsRead()
	{
		var form = FormWith(" 08:00:00 ", "09:00:00  ");

		form.Problem.Should().Be(WallClockRangeProblem.None);
		form.Bounds.Should().Be((_day.AddHours(8), _day.AddHours(9)));
	}

	[Fact]
	public void TheBounds_SpanDaysAndCarryNoConversion()
	{
		var form = FormWith("22:30:15", "01:00:00", endDayOffset: 1);

		form.Bounds.Should().Be(
			(new DateTime(2026, 10, 13, 22, 30, 15), new DateTime(2026, 10, 14, 1, 0, 0)));
		form.Bounds!.Value.From.Kind.Should().Be(DateTimeKind.Unspecified);
	}

	[Fact]
	public void ADateWithATimeOfDay_ContributesOnlyItsDay()
	{
		var form = FormWith("08:00:00", "09:00:00");

		form.StartDate = new DateTime(2026, 10, 13, 17, 45, 0);

		form.Bounds!.Value.From.Should().Be(_day.AddHours(8));
	}

	[Fact]
	public void AnEndNotAfterTheStart_GivesNoBounds()
	{
		var form = FormWith("09:00:00", "09:00:00");

		form.Bounds.Should().BeNull();
	}

	[Fact]
	public void EveryFieldSetter_CallsTheDerivedHook()
	{
		var form = FormWith("08:00:00", "09:00:00");
		var before = form.DerivedRaiseCount;

		form.StartDate = _day.AddDays(-1);
		form.StartTimeText = "07:00:00";
		form.EndDate = _day.AddDays(1);
		form.EndTimeText = "10:00:00";

		form.DerivedRaiseCount.Should().Be(before + 4);
	}

	private sealed class RangeOnlyForm : WallClockRangeForm
	{
		public int DerivedRaiseCount { get; private set; }

		public WallClockRangeProblem Problem => RangeProblem;

		public (DateTime From, DateTime To)? Bounds => RangeBounds;

		protected override void RaiseDerived()
		{
			DerivedRaiseCount++;
		}
	}
}
