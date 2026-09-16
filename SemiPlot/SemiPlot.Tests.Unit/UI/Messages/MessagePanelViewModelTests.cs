using AwesomeAssertions;

using SemiPlot.UI.Messages;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Messages;

// The panel touches no Avalonia type, so these are plain [Fact]. The clock is stepped by hand because a
// tight loop of reports would otherwise share one timestamp and hide a last-seen stamp that never moves.
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class MessagePanelViewModelTests
{
	[Fact]
	public void Report_CoalescesAnIdenticalFailureIntoOneEntry()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		for (var repeat = 0; repeat < 20; repeat++)
		{
			panel.Report(Failure("archive"));
		}

		panel.Entries.Should().HaveCount(1);
		panel.Entries[0].RepeatCount.Should().Be(20);
		panel.Entries[0].HasRepeated.Should().BeTrue();
		panel.Entries[0].LastSeen.Should().Be(SteppingClock.Origin + (SteppingClock.Step * 19));
	}

	[Fact]
	public void Report_KeepsTwoDifferentFailuresApart()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		panel.Report(Failure("archive"));
		panel.Report(Failure("catalogue"));

		panel.Entries.Should().HaveCount(2);
		panel.Entries.Select(entry => entry.View.Title).Should().Equal("catalogue", "archive");
		panel.Entries.Should().OnlyContain(entry => entry.RepeatCount == 1);
	}

	// Coalescing is against the newest entry only, so a failure that comes back after another one starts
	// a new row rather than reviving the old one.
	[Fact]
	public void Report_StartsANewEntryWhenAnotherFailureCameBetween()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		panel.Report(Failure("archive"));
		panel.Report(Failure("catalogue"));
		panel.Report(Failure("archive"));

		panel.Entries.Should().HaveCount(3);
		panel.Entries.Select(entry => entry.View.Title).Should().Equal("archive", "catalogue", "archive");
	}

	[Fact]
	public void Report_DropsTheOldestEntryOnceTheCapIsReached()
	{
		var clock = new SteppingClock();
		using var panel = new MessagePanelViewModel(clock);

		for (var index = 0; index <= MessagePanelViewModel.MaximumEntries; index++)
		{
			panel.Report(Failure($"failure {index}"));
		}

		panel.Entries.Should().HaveCount(MessagePanelViewModel.MaximumEntries);
		panel.Entries[0].View.Title.Should().Be($"failure {MessagePanelViewModel.MaximumEntries}");
		panel.Entries[^1].View.Title.Should().Be("failure 1");
	}

	[Fact]
	public void IsVisible_StartsClosedAndFollowsTheToggle()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());

		panel.IsVisible.Should().BeFalse("a session that never fails is not given the row");
		panel.HasEntries.Should().BeFalse();

		panel.ToggleCommand.Execute().Subscribe();

		panel.IsVisible.Should().BeTrue("one click on an empty panel still moves what is on screen");

		panel.ToggleCommand.Execute().Subscribe();

		panel.IsVisible.Should().BeFalse();
	}

	// The row exists to carry failures, so a failure that is not already on it opens it. A repeat of the
	// entry on top does not, or a panel closed during an outage would reopen on every reissued query.
	[Fact]
	public void Report_OpensThePanelForANewFailureAndLeavesARepeatWhereItIs()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());

		panel.Report(Failure("archive"));

		panel.IsVisible.Should().BeTrue("the entry would otherwise land off screen");

		panel.ToggleCommand.Execute().Subscribe();
		panel.Report(Failure("archive"));

		panel.Entries.Should().ContainSingle().Which.RepeatCount.Should().Be(2);
		panel.IsVisible.Should().BeFalse("the operator closed the panel on this failure");

		panel.Report(Failure("catalogue"));

		panel.IsVisible.Should().BeTrue("a failure the list does not carry yet opens it again");
	}

	[Fact]
	public void HasEntries_NotifiesWhenTheFirstEntryArrives()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());
		var raised = new List<string?>();
		panel.PropertyChanged += (_, arguments) => raised.Add(arguments.PropertyName);

		panel.Report(Failure("archive"));

		raised.Should().Contain(nameof(MessagePanelViewModel.HasEntries));
	}

	// The answer ResultReporting picks its log level from, so the log and the entry count cannot drift apart.
	[Fact]
	public void Report_AnswersWhetherItCoalescedAgainstTheNewestEntryOnly()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());
		var archive = Failure("archive");

		panel.Report(archive).Should().BeFalse("the list is empty");
		panel.Report(archive).Should().BeTrue();
		panel.Report(Failure("catalogue")).Should().BeFalse();
		panel.Report(archive).Should().BeFalse("another failure is now the newest entry");
	}

	[Fact]
	public void ClearCommand_EmptiesTheListAndLeavesThePanelOnScreen()
	{
		using var panel = new MessagePanelViewModel(new SteppingClock());
		panel.Report(Failure("archive"));

		panel.ClearCommand.Execute().Subscribe();

		panel.Entries.Should().BeEmpty();
		panel.HasEntries.Should().BeFalse();
		panel.IsVisible.Should().BeTrue();
	}

	private static ArchiveFailureView Failure(string title)
	{
		return new ArchiveFailureView(title, "detail", "remedy", MessageSeverity.Warning);
	}

	private sealed class SteppingClock : TimeProvider
	{
		public static readonly DateTimeOffset Origin = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

		public static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

		private int _reads;

		public override DateTimeOffset GetUtcNow()
		{
			return Origin + (Step * _reads++);
		}
	}
}
