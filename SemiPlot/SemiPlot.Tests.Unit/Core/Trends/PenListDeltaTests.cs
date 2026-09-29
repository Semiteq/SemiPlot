using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenListDeltaTests
{
	private static readonly Pen _pressure = new(
		1,
		"Chamber pressure",
		["Vacuum", "Watchlist"],
		"#112233",
		"Pa",
		"0.0",
		EnabledOnStart: true,
		ScaleMin: 0,
		ScaleMax: 100);

	private static readonly Pen _heater = new(2, "Heater", ["Heaters"], "#223344", "degC");

	private static readonly Pen _flow = new(3, "Gas flow", [], "#334455");

	// PenId is left out: a new id is another pen, so the delta reads it as a removal and an addition, which
	// ANewPen and AGonePen cover.
	public static TheoryData<string> RevisableMembers =>
	[
		nameof(Pen.Name),
		nameof(Pen.Groups),
		nameof(Pen.Color),
		nameof(Pen.Unit),
		nameof(Pen.Format),
		nameof(Pen.EnabledOnStart),
		nameof(Pen.ScaleMin),
		nameof(Pen.ScaleMax),
		nameof(Pen.LineStyle)
	];

	[Fact]
	public void EveryMemberOfPen_HasARevisionRow()
	{
		var rows = RevisableMembers.Select(row => row.Data).Append(nameof(Pen.PenId));

		typeof(Pen).GetProperties().Select(property => property.Name).Should().BeEquivalentTo(
			rows, "a member Equals leaves out would read as no change, so each one needs a row below");
	}

	[Theory]
	[MemberData(nameof(RevisableMembers))]
	public void AChangeToOneMember_GivesOneRevisionAndADifferentHash(string member)
	{
		var changed = WithOneMemberChanged(_pressure, member);

		var delta = PenListDelta.Between([_pressure], [changed]);

		changed.Should().NotBe(_pressure);
		changed.GetHashCode().Should().NotBe(_pressure.GetHashCode());
		delta.Revised.Should().ContainSingle().Which.Current.Should().BeSameAs(changed);
	}

	[Fact]
	public void TwoReadsWhoseGroupsAreEqualButDistinctLists_GiveAnEmptyDelta()
	{
		var reread = _pressure with { Groups = [.. _pressure.Groups] };

		reread.Groups.Should().NotBeSameAs(_pressure.Groups);
		reread.Should().Be(_pressure);
		reread.GetHashCode().Should().Be(_pressure.GetHashCode());

		var delta = PenListDelta.Between([_pressure, _heater], [reread, _heater with { Groups = ["Heaters"] }]);

		delta.IsEmpty.Should().BeTrue();
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Fact]
	public void AColourChange_GivesOneRevisionAndNoSetChange()
	{
		var recoloured = _heater with { Color = "#ABCDEF" };

		var delta = PenListDelta.Between([_pressure, _heater], [_pressure, recoloured]);

		var revision = delta.Revised.Should().ContainSingle().Which;

		revision.Previous.Should().Be(_heater);
		revision.Current.Should().Be(recoloured);
		revision.ScaleChanged.Should().BeFalse();
		delta.IsEmpty.Should().BeFalse();
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Theory]
	[InlineData(0.0, 50.0)]
	[InlineData(10.0, 100.0)]
	[InlineData(null, null)]
	public void AStoredScaleChange_GivesARevisionWithScaleChanged(double? scaleMin, double? scaleMax)
	{
		var rescaled = _pressure with { ScaleMin = scaleMin, ScaleMax = scaleMax };

		var delta = PenListDelta.Between([_pressure], [rescaled]);

		delta.Revised.Should().ContainSingle().Which.ScaleChanged.Should().BeTrue();
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Fact]
	public void AGroupsChange_GivesARevision()
	{
		var regrouped = _pressure with { Groups = ["Vacuum"] };

		var delta = PenListDelta.Between([_pressure], [regrouped]);

		delta.Revised.Should().ContainSingle().Which.Current.Groups.Should().Equal("Vacuum");
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Fact]
	public void ARename_GivesARevision()
	{
		var renamed = _heater with { Name = "Heater 1" };

		var delta = PenListDelta.Between([_heater], [renamed]);

		delta.Revised.Should().ContainSingle().Which.Current.Name.Should().Be("Heater 1");
		delta.ChangesPenSet.Should().BeFalse();
	}

	[Fact]
	public void AnEnabledOnStartChange_GivesARevision()
	{
		var hidden = _pressure with { EnabledOnStart = false };

		var delta = PenListDelta.Between([_pressure], [hidden]);

		var revision = delta.Revised.Should().ContainSingle().Which;

		revision.Current.EnabledOnStart.Should().BeFalse();
		revision.ScaleChanged.Should().BeFalse();
	}

	[Fact]
	public void ANewPen_IsAddedAndChangesThePenSet()
	{
		var delta = PenListDelta.Between([_pressure], [_pressure, _heater]);

		delta.Added.Should().Equal(_heater);
		delta.RemovedPenIds.Should().BeEmpty();
		delta.Revised.Should().BeEmpty();
		delta.ChangesPenSet.Should().BeTrue();
	}

	[Fact]
	public void AGonePen_IsRemovedAndChangesThePenSet()
	{
		var delta = PenListDelta.Between([_pressure, _heater], [_pressure]);

		delta.RemovedPenIds.Should().Equal(_heater.PenId);
		delta.Added.Should().BeEmpty();
		delta.Revised.Should().BeEmpty();
		delta.ChangesPenSet.Should().BeTrue();
	}

	[Fact]
	public void Current_KeepsTheOrderOfTheRead()
	{
		var delta = PenListDelta.Between([_pressure, _heater, _flow], [_flow, _pressure, _heater]);

		delta.Current.Select(pen => pen.PenId).Should().Equal(_flow.PenId, _pressure.PenId, _heater.PenId);
		delta.IsEmpty.Should().BeTrue();
	}

	[Fact]
	public void AnEmptyPreviousList_GivesEveryPenAdded()
	{
		var delta = PenListDelta.Between([], [_pressure, _heater]);

		delta.Added.Should().Equal(_pressure, _heater);
		delta.ChangesPenSet.Should().BeTrue();
	}

	[Fact]
	public void AnEmptyCurrentList_GivesEveryPenRemoved()
	{
		var delta = PenListDelta.Between([_pressure, _heater], []);

		delta.RemovedPenIds.Should().Equal(_pressure.PenId, _heater.PenId);
		delta.Current.Should().BeEmpty();
		delta.ChangesPenSet.Should().BeTrue();
	}

	private static Pen WithOneMemberChanged(Pen pen, string member)
	{
		return member switch
		{
			nameof(Pen.Name) => pen with { Name = pen.Name + " 2" },
			nameof(Pen.Groups) => pen with { Groups = [.. pen.Groups, "Spare"] },
			nameof(Pen.Color) => pen with { Color = "#FEDCBA" },
			nameof(Pen.Unit) => pen with { Unit = "kPa" },
			nameof(Pen.Format) => pen with { Format = "0.000" },
			nameof(Pen.EnabledOnStart) => pen with { EnabledOnStart = !pen.EnabledOnStart },
			nameof(Pen.ScaleMin) => pen with { ScaleMin = -5.0 },
			nameof(Pen.ScaleMax) => pen with { ScaleMax = 500.0 },
			nameof(Pen.LineStyle) => pen with { LineStyle = PenLineStyle.Stepped },
			_ => throw new ArgumentOutOfRangeException(nameof(member), member, "No change is defined for it.")
		};
	}
}
