using AwesomeAssertions;

using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TimeWindowFormTests
{
	private static readonly TimeZoneInfo _berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	private static readonly DateTime _fromUtc = new(2026, 10, 13, 6, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _day = new(2026, 10, 13);

	private static TimeWindowForm SeededForm()
	{
		var form = new TimeWindowForm(_berlin);
		form.Seed(_fromUtc, _fromUtc.AddMinutes(5));

		return form;
	}

	[Fact]
	public void Seed_ShowsTheLocalEdgesFlooredToTheSecondAndTheWidth()
	{
		var form = new TimeWindowForm(_berlin);

		form.Seed(_fromUtc.AddMilliseconds(900), _fromUtc.AddMinutes(5).AddMilliseconds(900));

		form.StartDate.Should().Be(_day);
		form.StartTimeText.Should().Be("08:00:00");
		form.EndDate.Should().Be(_day);
		form.EndTimeText.Should().Be("08:05:00");
		form.WidthText.Should().Be("0:05:00");
	}

	[Fact]
	public void Seed_PutsAnEdgeOnItsLocalDay()
	{
		var form = new TimeWindowForm(_berlin);

		var fromUtc = new DateTime(2026, 10, 13, 21, 30, 0, DateTimeKind.Utc);

		form.Seed(fromUtc, fromUtc.AddHours(1));

		form.StartDate.Should().Be(_day);
		form.StartTimeText.Should().Be("23:30:00");
		form.EndDate.Should().Be(_day.AddDays(1));
		form.EndTimeText.Should().Be("00:30:00");
	}

	[Fact]
	public void ASeededForm_IsValidAndDiffersFromNothing()
	{
		var form = SeededForm();

		form.Problem.Should().Be(TimeWindowProblem.None);
		form.Message.Should().BeEmpty();
		form.EdgesDifferFromSeed.Should().BeFalse();
		form.WidthDiffersFromSeed.Should().BeFalse();
	}

	[Fact]
	public void ASubSecondSeed_DiffersFromNothingUntilAFieldChanges()
	{
		var form = new TimeWindowForm(_berlin);
		form.Seed(_fromUtc.AddMilliseconds(400), _fromUtc.AddMinutes(5));

		form.EdgesDifferFromSeed.Should().BeFalse();

		form.StartTimeText = "07:59:00";
		form.EdgesDifferFromSeed.Should().BeTrue();

		form.StartTimeText = "08:00:00";
		form.EdgesDifferFromSeed.Should().BeFalse();
	}

	[Fact]
	public void ADateChange_DiffersFromTheSeedAndATimeOfDayOnThePickedDateDoesNot()
	{
		var form = SeededForm();

		form.EndDate = _day.AddHours(13);
		form.EdgesDifferFromSeed.Should().BeFalse();

		form.EndDate = _day.AddDays(1);
		form.EdgesDifferFromSeed.Should().BeTrue();
	}

	[Fact]
	public void AWidthChange_DiffersFromTheSeedAndLeavesTheEdgesUnchanged()
	{
		var form = SeededForm();

		form.WidthText = "0:10:00";

		form.WidthDiffersFromSeed.Should().BeTrue();
		form.EdgesDifferFromSeed.Should().BeFalse();
	}

	[Fact]
	public void AValidPair_ReturnsUtcEdges()
	{
		var form = SeededForm();

		form.StartTimeText = "07:00:00";
		form.EndDate = _day.AddDays(1);
		form.EndTimeText = "09:30:15";

		form.From.Should().Be(new DateTime(2026, 10, 13, 5, 0, 0, DateTimeKind.Utc));
		form.To.Should().Be(new DateTime(2026, 10, 14, 7, 30, 15, DateTimeKind.Utc));
		form.From!.Value.Kind.Should().Be(DateTimeKind.Utc);
		form.To!.Value.Kind.Should().Be(DateTimeKind.Utc);
	}

	[Fact]
	public void APickerDateOfLocalKind_ConvertsAsWallClockInTheFormZone()
	{
		var form = SeededForm();

		form.StartDate = DateTime.SpecifyKind(_day, DateTimeKind.Local);

		form.From.Should().Be(_fromUtc);
	}

	[Fact]
	public void Problems_ComeInPanelOrder()
	{
		var springForward = new DateTime(2026, 3, 29);
		var form = new TimeWindowForm(_berlin) { StartTimeText = "junk", EndTimeText = "junk", WidthText = "abc" };
		form.Problem.Should().Be(TimeWindowProblem.StartDateMissing);

		form.StartDate = springForward;
		form.Problem.Should().Be(TimeWindowProblem.StartUnreadable);

		form.StartTimeText = "02:30:00";
		form.Problem.Should().Be(TimeWindowProblem.EndDateMissing);

		form.EndDate = springForward;
		form.Problem.Should().Be(TimeWindowProblem.EndUnreadable);

		form.EndTimeText = "01:00:00";
		form.Problem.Should().Be(TimeWindowProblem.EndNotAfterStart);

		form.EndDate = springForward.AddDays(366);
		form.Problem.Should().Be(TimeWindowProblem.TimeSkipped);

		form.StartTimeText = "01:00:00";
		form.Problem.Should().Be(TimeWindowProblem.SpanOverLimit);

		form.EndDate = springForward.AddDays(1);
		form.Problem.Should().Be(TimeWindowProblem.WidthUnreadable);

		form.WidthText = "0:00:00";
		form.Problem.Should().Be(TimeWindowProblem.WidthOutOfRange);

		form.WidthText = "0:05:00";
		form.Problem.Should().Be(TimeWindowProblem.None);
	}

	[Fact]
	public void EveryProblem_HasAMessage()
	{
		var form = SeededForm();

		form.WidthText = "0:00:00";
		form.Message.Should().Contain("0:00:01").And.Contain("8760:00:00");

		form.WidthText = "abc";
		form.Message.Should().Contain("h:mm:ss");

		form.StartTimeText = "junk";
		form.Message.Should().Contain("HH:mm:ss");

		form.StartTimeText = "08:00:00";
		form.EndDate = _day.AddDays(366);
		form.Message.Should().Contain("365");

		form.EndDate = null;
		form.Message.Should().NotBeEmpty();
	}

	[Theory]
	[InlineData("0:00:01")]
	[InlineData("8760:00:00")]
	[InlineData(" 1:00:00 ")]
	public void AWidthInsideTheLimits_IsRead(string text)
	{
		var form = SeededForm();

		form.WidthText = text;

		form.Problem.Should().Be(TimeWindowProblem.None);
		form.IsWidthInvalid.Should().BeFalse();
		form.Width.Should().NotBeNull();
	}

	[Theory]
	[InlineData("0:00:00")]
	[InlineData("8760:00:01")]
	public void AWidthOutsideTheLimits_IsAProblem(string text)
	{
		var form = SeededForm();

		form.WidthText = text;

		form.Problem.Should().Be(TimeWindowProblem.WidthOutOfRange);
		form.IsWidthInvalid.Should().BeTrue();
		form.Width.Should().BeNull();
	}

	[Fact]
	public void AWidth_IsReadExactly()
	{
		var form = SeededForm();

		form.WidthText = "168:00:00";

		form.Width.Should().Be(TimeSpan.FromDays(7));
	}

	[Fact]
	public void ABadWidth_LeavesTheEdgesValid()
	{
		var form = SeededForm();

		form.WidthText = "abc";

		form.From.Should().Be(_fromUtc);
		form.To.Should().Be(_fromUtc.AddMinutes(5));
	}

	[Fact]
	public void ABadPair_LeavesTheWidthValid()
	{
		var form = SeededForm();

		form.StartTimeText = "junk";

		form.From.Should().BeNull();
		form.To.Should().BeNull();
		form.Width.Should().Be(TimeSpan.FromMinutes(5));
	}

	[Fact]
	public void ASpanOfExactly365Days_IsValid()
	{
		var form = new TimeWindowForm(_berlin)
		{
			StartDate = new DateTime(2026, 1, 10),
			StartTimeText = "12:00:00",
			EndDate = new DateTime(2027, 1, 10),
			EndTimeText = "12:00:00",
			WidthText = "1:00:00"
		};

		form.Problem.Should().Be(TimeWindowProblem.None);
		(form.To - form.From).Should().Be(TimeSpan.FromDays(365));
	}

	[Fact]
	public void ASpanOverOneYear_IsRefusedAndFlagsTheEnd()
	{
		var form = new TimeWindowForm(_berlin)
		{
			StartDate = new DateTime(2026, 1, 10),
			StartTimeText = "12:00:00",
			EndDate = new DateTime(2027, 1, 10),
			EndTimeText = "12:00:01",
			WidthText = "1:00:00"
		};

		form.Problem.Should().Be(TimeWindowProblem.SpanOverLimit);
		form.From.Should().BeNull();
		form.IsEndDateInvalid.Should().BeTrue();
		form.IsEndTimeInvalid.Should().BeTrue();
		form.IsStartTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void TheSpan_IsMeasuredInUtc()
	{
		// Winter start, summer end: the wall-clock span runs one hour longer than the UTC one.
		var form = new TimeWindowForm(_berlin)
		{
			StartDate = new DateTime(2026, 3, 28),
			StartTimeText = "12:00:00",
			EndDate = new DateTime(2027, 3, 28),
			EndTimeText = "12:30:00",
			WidthText = "1:00:00"
		};

		form.Problem.Should().Be(TimeWindowProblem.None);
		(form.To - form.From).Should().Be(TimeSpan.FromDays(365) - TimeSpan.FromMinutes(30));
	}

	[Fact]
	public void ASkippedSpringForwardTime_IsAFormProblem()
	{
		var form = SeededForm();

		form.StartDate = new DateTime(2026, 3, 29);
		form.StartTimeText = "02:30:00";

		form.Problem.Should().Be(TimeWindowProblem.TimeSkipped);
		form.From.Should().BeNull();
		form.IsStartTimeInvalid.Should().BeTrue();
		form.IsEndTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void ASkippedEndTime_FlagsTheEndTime()
	{
		var form = SeededForm();

		form.StartDate = new DateTime(2026, 3, 28);
		form.EndDate = new DateTime(2026, 3, 29);
		form.EndTimeText = "02:00:00";

		form.Problem.Should().Be(TimeWindowProblem.TimeSkipped);
		form.IsEndTimeInvalid.Should().BeTrue();
		form.IsStartTimeInvalid.Should().BeFalse();
	}

	[Fact]
	public void AnAmbiguousAutumnTime_TakesTheStandardOffset()
	{
		var form = SeededForm();

		form.StartDate = new DateTime(2026, 10, 25);
		form.StartTimeText = "02:30:00";
		form.EndDate = new DateTime(2026, 10, 25);
		form.EndTimeText = "04:00:00";

		form.From.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	public void EveryFieldSetter_RaisesTheDerivedProperties()
	{
		var form = SeededForm();
		var raised = new List<string?>();
		form.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

		form.WidthText = "0:10:00";
		form.StartTimeText = "07:00:00";

		raised.Should()
			.Contain(nameof(TimeWindowForm.Width))
			.And.Contain(nameof(TimeWindowForm.IsWidthInvalid))
			.And.Contain(nameof(TimeWindowForm.From))
			.And.Contain(nameof(TimeWindowForm.Problem))
			.And.Contain(nameof(TimeWindowForm.Message));
	}
}
