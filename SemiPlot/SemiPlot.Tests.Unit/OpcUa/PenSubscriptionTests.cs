using System.Reactive;
using System.Reactive.Subjects;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Opc.Ua;
using Opc.Ua.Client;

using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

// No session: the stack raises an item's Notification from SaveValueInCache, and the queued work runs when the test
// drains the loop.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenSubscriptionTests : IDisposable
{
	private const string Endpoint = "opc.tcp://127.0.0.1:4841";
	private const string ProjectId = "FD34514A35";
	private const ushort NamespaceIndex = 3;

	private static readonly DateTime _firstUtc = new(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _secondUtc = _firstUtc.AddSeconds(30);

	private readonly SerialLoop _loop = new(NullLogger.Instance);
	private readonly DeliveredStamps _stamps = new();
	private readonly Subject<LiveSourceError> _penFaults = new();
	private readonly List<LiveSourceError> _faults = [];
	private readonly List<Sample> _samples = [];
	private readonly IDisposable _faultRecording;

	public PenSubscriptionTests()
	{
		_faultRecording = _penFaults.Subscribe(_faults.Add);
	}

	public void Dispose()
	{
		_faultRecording.Dispose();
		_penFaults.Dispose();
	}

	[Fact]
	public async Task ABadNotification_EmitsABreakAtItsStamp()
	{
		var (_, subscription) = Attached(1);
		var bad = new DataValue(Variant.From(2.5), StatusCodes.BadCommunicationError, _secondUtc, _secondUtc);

		Notify(subscription, 1, new DataValue(Variant.From(2.5), StatusCodes.Good, _firstUtc, _firstUtc));
		Notify(subscription, 1, bad);
		await DrainAsync();

		_samples.Should().Equal(new Sample(1, _firstUtc, 2.5), new Sample(1, _secondUtc, null));
		_faults.Should().BeEmpty();
	}

	[Fact]
	public async Task ANonNumericNotification_ReportsThePenAsNotNumeric_AndAnUnstampedOneCountsAsAnswered()
	{
		var (_, subscription) = Attached(1, 2);

		Notify(subscription, 1, new DataValue(Variant.From(2.5)));
		Notify(subscription, 2, new DataValue(Variant.From("12.5"), StatusCodes.Good, _firstUtc, _firstUtc));
		await DrainAsync();

		_samples.Should().BeEmpty("neither notification carries a number to place");
		var fault = _faults.Should().ContainSingle("pen 1 answered, so every pen has").Subject;
		fault.Kind.Should().Be(LiveSourceFault.PensUnserved);
		fault.UnknownPenIds.Should().BeEmpty();
		fault.NotNumericPenIds.Should().Equal(2);
	}

	[Fact]
	public void AClosedSubscription_EmitsNoLossBreak()
	{
		var pen = new PenSubscription([1], Recorder(), Context());
		_stamps.RecordValue(1, _firstUtc);

		pen.Close();
		pen.EmitLossBreaks();

		_samples.Should().BeEmpty();
	}

	[Fact]
	public async Task ANotificationQueuedBeforeTheClose_EmitsNothing()
	{
		var (pen, subscription) = Attached(1);

		Notify(subscription, 1, new DataValue(Variant.From(2.5), StatusCodes.Good, _firstUtc, _firstUtc));
		pen.Close();
		await DrainAsync();

		_samples.Should().BeEmpty();
	}

	private static void Notify(Subscription subscription, int penId, DataValue value)
	{
		subscription.MonitoredItems
			.Single(item => item.Handle is int handle && handle == penId)
			.SaveValueInCache(new MonitoredItemNotification { Value = value });
	}

	private (PenSubscription Pen, Subscription Subscription) Attached(params int[] penIds)
	{
		var pen = new PenSubscription(penIds, Recorder(), Context());
		var subscription = pen.Build(ProjectId, NamespaceIndex);
		pen.Attach(subscription);

		return (pen, subscription);
	}

	private IObserver<IReadOnlyList<Sample>> Recorder()
	{
		return Observer.Create<IReadOnlyList<Sample>>(_samples.AddRange);
	}

	private PenSubscriptionContext Context()
	{
		return TestPenSubscriptionContext.Create(_loop, _stamps, _penFaults, Endpoint);
	}

	private async Task DrainAsync()
	{
		_loop.Complete();
		await _loop.PumpAsync(TestContext.Current.CancellationToken);
	}
}
