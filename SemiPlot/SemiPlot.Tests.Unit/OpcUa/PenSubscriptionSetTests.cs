using System.Reactive;
using System.Reactive.Subjects;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

// No session ever connects: the test records the stamps a delivered value would, and the queued work runs on a loop
// the test pumps.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenSubscriptionSetTests : IDisposable
{
	private const string Endpoint = "opc.tcp://127.0.0.1:4841";

	private static readonly DateTime _firstUtc = new(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _secondUtc = _firstUtc.AddSeconds(30);
	private static readonly TimeSpan _oneMillisecond = TimeSpan.FromMilliseconds(1);

	private readonly SerialLoop _loop = new(NullLogger.Instance);
	private readonly DeliveredStamps _stamps = new();
	private readonly Subject<LiveSourceError> _penFaults = new();
	private readonly PenSubscriptionSet _set;

	public PenSubscriptionSetTests()
	{
		_set = new PenSubscriptionSet(
			TestPenSubscriptionContext.Create(_loop, _stamps, _penFaults, Endpoint),
			CancellationToken.None);
	}

	public void Dispose()
	{
		_penFaults.Dispose();
	}

	// The coordinator's Switch closes the old set at once while its removal waits behind a loss already queued.
	[Fact]
	public async Task ALossQueuedAheadOfAPenSetChange_BreaksTheKeptPensOnTheNewSet()
	{
		var closedSamples = new List<Sample>();
		var keptSamples = new List<Sample>();
		var held = new TaskCompletionSource();
		var pump = _loop.PumpAsync(TestContext.Current.CancellationToken);
		var first = _set.Open([1, 2], Observer.Create<IReadOnlyList<Sample>>(closedSamples.AddRange));
		await _loop.InvokeAsync(() =>
		{
			_stamps.RecordValue(1, _firstUtc);
			_stamps.RecordValue(2, _secondUtc);
		});

		_loop.Enqueue(() => held.Task);
		_loop.Enqueue(_set.OnLost);
		first.Dispose();
		_set.Open([1, 2], Observer.Create<IReadOnlyList<Sample>>(keptSamples.AddRange));
		held.SetResult();
		_loop.Complete();
		await pump;

		closedSamples.Should().BeEmpty();
		keptSamples.Should().Equal(
			new Sample(1, _firstUtc + _oneMillisecond, null),
			new Sample(2, _secondUtc + _oneMillisecond, null));
	}

	[Fact]
	public async Task ASetAddedWhileTheSessionIsDown_BreaksOnlyPensWithADeliveredValueOnce()
	{
		var samples = new List<Sample>();
		var other = new List<Sample>();
		_stamps.RecordValue(1, _firstUtc);

		_set.Open([1, 3], Observer.Create<IReadOnlyList<Sample>>(samples.AddRange));
		_set.Open([1], Observer.Create<IReadOnlyList<Sample>>(other.AddRange));
		await DrainAsync();

		samples.Should().Equal(new Sample(1, _firstUtc + _oneMillisecond, null));
		other.Should().BeEmpty("the pen's last emission is already a break");
	}

	private async Task DrainAsync()
	{
		_loop.Complete();
		await _loop.PumpAsync(TestContext.Current.CancellationToken);
	}
}
