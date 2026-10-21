using AwesomeAssertions;

using Opc.Ua;
using Opc.Ua.Client;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class LiveSessionStatesTests
{
	private const string Endpoint = "opc.tcp://127.0.0.1:4841";

	private readonly LiveSessionStates _states = new(Endpoint);

	[Fact]
	public void TheFirstConnect_PublishesConnected()
	{
		var change = _states.OnStateChanged(ConnectionState.Connected);

		change.Published.Should().Be(LiveSourceState.Connected);
		change.IsLoss.Should().BeFalse();
	}

	[Theory]
	[InlineData(ConnectionState.Reconnecting)]
	[InlineData(ConnectionState.Disconnected)]
	[InlineData(ConnectionState.Closing)]
	[InlineData(ConnectionState.Closed)]
	public void LeavingConnected_PublishesTheUnreachableFaultAndOneLoss(ConnectionState next)
	{
		_states.OnStateChanged(ConnectionState.Connected);

		var change = _states.OnStateChanged(next);
		var repeated = _states.OnStateChanged(next);

		change.IsLoss.Should().BeTrue();
		FaultOf(change).Kind.Should().Be(LiveSourceFault.Unreachable);
		FaultOf(change).Endpoint.Should().Be(Endpoint);
		repeated.Should().Be(LiveSessionChange.None);
	}

	[Fact]
	public void LeavingAStateBeforeAnyConnect_PublishesNothingAndLosesNothing()
	{
		_states.OnStateChanged(ConnectionState.Disconnected).Should().Be(LiveSessionChange.None);
	}

	[Fact]
	public void ConnectedAfterAFault_PublishesConnected()
	{
		_states.OnStateChanged(ConnectionState.Connected);
		_states.OnStateChanged(ConnectionState.Reconnecting);

		var change = _states.OnStateChanged(ConnectionState.Connected);

		change.Published.Should().Be(LiveSourceState.Connected);
		change.IsLoss.Should().BeFalse();
	}

	[Fact]
	public void ConnectedTwice_PublishesOnce()
	{
		_states.OnStateChanged(ConnectionState.Connected);

		_states.OnStateChanged(ConnectionState.Connected).Should().Be(LiveSessionChange.None);
	}

	[Fact]
	public void TheGraceWithNoSession_PublishesTheUnreachableFaultOnce()
	{
		var change = _states.OnGraceElapsed();

		FaultOf(change).Kind.Should().Be(LiveSourceFault.Unreachable);
		change.IsLoss.Should().BeFalse();
		_states.OnGraceElapsed().Should().Be(LiveSessionChange.None);
	}

	[Fact]
	public void TheGraceAfterASession_PublishesNothing()
	{
		_states.OnStateChanged(ConnectionState.Connected);
		_states.OnStateChanged(ConnectionState.Reconnecting);

		_states.OnGraceElapsed().Should().Be(LiveSessionChange.None);
	}

	[Theory]
	[InlineData(LiveSourceFault.EndpointRefused)]
	[InlineData(LiveSourceFault.ProjectMissing)]
	public void TheGraceAfterASetupFault_KeepsTheSetupFault(LiveSourceFault kind)
	{
		_states.OnSetupFault(kind);

		_states.OnGraceElapsed().Should().Be(LiveSessionChange.None);
		_states.OnSetupFault(kind).Should().Be(LiveSessionChange.None, "the fault the state holds is still {0}", kind);
	}

	[Fact]
	public void ASetupFault_PublishesOnceForTheSameKind()
	{
		var first = _states.OnSetupFault(LiveSourceFault.ProjectMissing);
		var again = _states.OnSetupFault(LiveSourceFault.ProjectMissing);

		FaultOf(first).Kind.Should().Be(LiveSourceFault.ProjectMissing);
		FaultOf(first).Endpoint.Should().Be(Endpoint);
		again.Should().Be(LiveSessionChange.None);
	}

	[Fact]
	public void ASetupFaultOfAnotherKind_PublishesTheNewKind()
	{
		_states.OnSetupFault(LiveSourceFault.EndpointRefused);

		var change = _states.OnSetupFault(LiveSourceFault.ProjectMissing);

		FaultOf(change).Kind.Should().Be(LiveSourceFault.ProjectMissing);
		change.IsLoss.Should().BeFalse();
	}

	[Theory]
	[InlineData(LiveSourceFault.EndpointRefused)]
	[InlineData(LiveSourceFault.ProjectMissing)]
	public void AnUnansweredPassAfterASetupFault_PublishesTheUnreachableFault(LiveSourceFault kind)
	{
		_states.OnSetupFault(kind);

		var change = _states.OnUnanswered();

		FaultOf(change).Kind.Should().Be(LiveSourceFault.Unreachable);
		change.IsLoss.Should().BeFalse();
		_states.OnUnanswered().Should().Be(LiveSessionChange.None);
	}

	[Fact]
	public void AnUnansweredPassWithinTheGrace_PublishesNothing()
	{
		_states.OnUnanswered().Should().Be(LiveSessionChange.None);
		FaultOf(_states.OnGraceElapsed()).Kind.Should().Be(LiveSourceFault.Unreachable);
	}

	[Fact]
	public void AnUnansweredPassThatStillHeldTheConnectedState_IsALoss()
	{
		_states.OnStateChanged(ConnectionState.Connected);

		var change = _states.OnUnanswered();

		FaultOf(change).Kind.Should().Be(LiveSourceFault.Unreachable);
		change.IsLoss.Should().BeTrue();
	}

	[Fact]
	public void ANewSessionId_AsksForABrowseAndTheSameIdDoesNot()
	{
		var first = new NodeId(17u, 1);
		var recreated = new NodeId(18u, 1);

		_states.IsNewSession(first).Should().BeTrue();
		_states.IsNewSession(first).Should().BeFalse();
		_states.IsNewSession(recreated).Should().BeTrue();
	}

	private static LiveSourceError FaultOf(LiveSessionChange change)
	{
		change.Published.Should().NotBeNull();
		change.Published.IsConnected.Should().BeFalse();
		change.Published.Fault.Should().NotBeNull();

		return change.Published.Fault;
	}
}
