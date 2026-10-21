using AwesomeAssertions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ConnectBackoffTests
{
	private const string Endpoint = "opc.tcp://127.0.0.1:4841";

	private readonly ConnectBackoff _backoff = new();

	[Fact]
	public void WhileNoServerAnswers_TheWaitDoublesFromOneSecondToTheFiveSecondCap()
	{
		var waits = Enumerable.Range(0, 5).Select(_ => _backoff.Next(ConnectPassEnd.Unanswered));

		waits.Select(wait => wait.TotalSeconds).Should().Equal(1, 2, 4, 5, 5);
	}

	// The two pieces the session's setup-fault path calls in turn, side by side.
	[Theory]
	[InlineData(LiveSourceFault.EndpointRefused)]
	[InlineData(LiveSourceFault.ProjectMissing)]
	public void ASetupFault_IsReportedOnce_AndRetriedAfterThirtySeconds(LiveSourceFault kind)
	{
		var states = new LiveSessionStates(Endpoint);

		var first = states.OnSetupFault(kind);
		var firstWait = _backoff.Next(ConnectPassEnd.SetupFault);
		var second = states.OnSetupFault(kind);
		var secondWait = _backoff.Next(ConnectPassEnd.SetupFault);

		(first.Published?.Fault?.Kind).Should().Be(kind);
		first.IsLoss.Should().BeFalse();
		second.Should().Be(LiveSessionChange.None);
		firstWait.Should().Be(TimeSpan.FromSeconds(30));
		secondWait.Should().Be(TimeSpan.FromSeconds(30));
	}

	[Fact]
	public void ASetupFault_StartsTheNextRunOfFailuresAtOneSecond()
	{
		FailThreeTimesThen(ConnectPassEnd.SetupFault);

		_backoff.Next(ConnectPassEnd.Unanswered).Should().Be(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public void AnEndedSession_StartsTheNextRunOfFailuresAtOneSecond()
	{
		FailThreeTimesThen(ConnectPassEnd.SessionEnded);

		_backoff.Next(ConnectPassEnd.Unanswered).Should().Be(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public void ASessionThatEndedOnItsOwn_IsRetriedAfterOneSecond()
	{
		_backoff.Next(ConnectPassEnd.SessionEnded).Should().Be(TimeSpan.FromSeconds(1));
	}

	private void FailThreeTimesThen(ConnectPassEnd end)
	{
		_backoff.Next(ConnectPassEnd.Unanswered);
		_backoff.Next(ConnectPassEnd.Unanswered);
		_backoff.Next(ConnectPassEnd.Unanswered);
		_backoff.Next(end);
	}
}
