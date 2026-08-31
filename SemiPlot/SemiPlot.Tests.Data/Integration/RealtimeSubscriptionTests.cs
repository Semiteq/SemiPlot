using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// Both tests append rows, so neither may take SeededArchive, whose contract is that the class leaves the
// database as it found it.
//
// Nothing here waits on a timeout. Every synchronisation point is an awaited emission: the Connected signal
// the subscription's first successful tick reports, or a batch that has been delivered. A subscription can
// never emit a row that already existed when it subscribed, so a test appending before the baseline has run
// would be racing it — and the losing side of that race is an await that never returns.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeSubscriptionTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private static readonly int[] _subscribedPenIds = [1, 2];

	// One calendar day, so the write creates the single partition tp2026m01d01 and every appended row falls
	// inside it. Winter under Europe/Berlin, so the conversion out is an unambiguous +1 h.
	private static readonly DateTime _day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _nextDay = _day.AddDays(1);

	// The newest seeded row, which is what each subscription's baseline answers. Everything appended sits
	// after it and hours short of the partition's upper bound.
	private static readonly DateTime _seededLast = _day.AddMinutes(1);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	private const string AppendCommand = """
		INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, 0, @t, @v, @q);
		""";

	protected override async ValueTask SeedAsync()
	{
		var written = await Writer().WriteAsync(SeededRows(), _day, _nextDay);

		Assert.True(written.IsSuccess, ArchiveReadSupport.Describe(written));
	}

	// TrendCoordinator publishes the batches through RefCount, which disposes the upstream when the last
	// subscriber goes, so a loop surviving its own disposal would keep querying for the rest of the process.
	// The second subscription is what proves the elapsed time: its own delivery cannot happen before a poll
	// interval has passed since the first was dropped.
	[Fact]
	public async Task DisposingASubscriptionStopsItsPoll()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		using var armed = new ArmedGate(provider.ConnectionFaults);
		using var first = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(1);

		var firstDelivery = first.NextBatch();

		await AppendAsync(_seededLast.AddSeconds(1), 1, cancellationToken);
		await firstDelivery;

		first.Dispose();

		var deliveredBeforeDisposal = first.Batches.Count;

		using var second = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(2);

		var secondDelivery = second.NextBatch();

		await AppendAsync(_seededLast.AddSeconds(2), 2, cancellationToken);
		await secondDelivery;

		Assert.True(
			armed.NoFaultWasSeen,
			"an armed point counted off the shared stream is the right subscription's only while no fault "
			+ "has been raised and cleared behind it");
		Assert.Equal(deliveredBeforeDisposal, first.Batches.Count);
	}

	// The sequence is cold, so each subscription runs a RealtimePoll of its own and takes a baseline of its
	// own. The second one's baseline is already past the row the first was delivered, so that row must never
	// reach it, while the row appended after both are armed reaches both.
	[Fact]
	public async Task TwoSubscriptionsEachKeepTheirOwnLastSeen()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var beforeTheSecondSubscription = _seededLast.AddSeconds(1);
		var afterBoth = _seededLast.AddSeconds(2);

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		using var armed = new ArmedGate(provider.ConnectionFaults);
		using var first = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(1);

		var firstSeesTheEarlyRow = first.NextBatch();

		await AppendAsync(beforeTheSecondSubscription, 1, cancellationToken);
		await firstSeesTheEarlyRow;

		using var second = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(2);

		var firstSeesTheLateRow = first.NextBatch();
		var secondSeesTheLateRow = second.NextBatch();

		await AppendAsync(afterBoth, 2, cancellationToken);
		await firstSeesTheLateRow;
		await secondSeesTheLateRow;

		Assert.True(
			armed.NoFaultWasSeen,
			"the second subscription's armed point is the second connected state only while no fault has "
			+ "been raised and cleared behind it");
		Assert.Equal(
			[_timeConverter.ToUtc(beforeTheSecondSubscription), _timeConverter.ToUtc(afterBoth)],
			Delivered(first));
		Assert.Equal([_timeConverter.ToUtc(afterBoth)], Delivered(second));
	}

	// The armed point is per subscription, not per provider: a consumer subscribing to a provider that has
	// already been polling for hours still has an event to sequence on. Nothing here appends, so the only
	// states the stream can carry are the two armed points.
	[Fact]
	public async Task EverySubscriptionReportsConnectedOnItsOwnFirstTick()
	{
		Fixture.RequireAvailable();

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		using var armed = new ArmedGate(provider.ConnectionFaults);
		using var first = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(1);

		using var second = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(2);

		Assert.Equal(2, armed.States.Count);
		Assert.All(armed.States, state => Assert.True(state.IsConnected));
	}

	private static DateTime[] Delivered(BatchCollector collector)
	{
		return collector.Batches
			.SelectMany(batch => batch)
			.Select(sample => sample.TimestampUtc)
			.ToArray();
	}

	// Written as scada_writer, the role the SCADA itself writes with, and one row at a time: a COPY would go
	// through ArchiveWriter, which refuses an archive already carrying rows.
	private async Task AppendAsync(DateTime archiveLocal, double value, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(AppendCommand, connection);

		command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer)
		{
			Value = _subscribedPenIds[0]
		});
		command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Timestamp) { Value = archiveLocal });
		command.Parameters.Add(new NpgsqlParameter("v", NpgsqlDbType.Double) { Value = value });
		command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Integer)
		{
			Value = ArchiveRow.OrdinaryQuality
		});

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	// Two rows per subscribed variable, the second of them the baseline every subscription starts from.
	private static IReadOnlyList<ArchiveRow> SeededRows()
	{
		var rows = new List<ArchiveRow>();

		foreach (var penId in _subscribedPenIds)
		{
			rows.Add(new ArchiveRow(penId, ArchiveRow.RawLayer, _day, penId, ArchiveRow.OrdinaryQuality));
			rows.Add(new ArchiveRow(penId, ArchiveRow.RawLayer, _seededLast, penId, ArchiveRow.OrdinaryQuality));
		}

		return rows;
	}

	// One collector for the whole test, taken before the first subscription. ConnectionFaults is provider-
	// wide and ArchiveConnectionState carries no discriminator, so which subscription a Connected belongs
	// to is inferred rather than read: the subscriptions here are started one at a time, and a subscription
	// reports Connected exactly once — on its own first successful tick — unless a fault is raised and
	// later cleared. The nth Connected is therefore the nth subscription's armed point, and NoFaultWasSeen
	// is what makes that an asserted precondition instead of an assumption. A per-subscription gate
	// completed by the first Connected it happened to see could be completed by another subscription's
	// recovery instead, arming a test before the subscription it is about has read its baseline.
	//
	// The waiters are completed from the poll's own thread, inside the connection stream's notification.
	// They run their continuations asynchronously because an inline one would resume the test body on that
	// thread and block the very loop the next await is waiting on (CLAUDE.md, "An xunit v3 test project is
	// an executable").
	private sealed class ArmedGate : IDisposable
	{
		private readonly List<ArchiveConnectionState> _states = [];

		private readonly List<(int Count, TaskCompletionSource Reached)> _waiters = [];

		private readonly Lock _guard = new();

		private readonly IDisposable _subscription;

		private int _connectedCount;

		public ArmedGate(IObservable<ArchiveConnectionState> states)
		{
			_subscription = states.Subscribe(Record);
		}

		public IReadOnlyList<ArchiveConnectionState> States
		{
			get
			{
				lock (_guard)
				{
					return _states.ToArray();
				}
			}
		}

		public bool NoFaultWasSeen => States.All(state => state.IsConnected);

		/// <summary>
		/// Completes once the stream has carried <paramref name="subscriptionCount"/> connected states.
		/// </summary>
		public Task Armed(int subscriptionCount)
		{
			lock (_guard)
			{
				if (_connectedCount >= subscriptionCount)
				{
					return Task.CompletedTask;
				}

				var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				_waiters.Add((subscriptionCount, waiter));

				return waiter.Task;
			}
		}

		public void Dispose()
		{
			_subscription.Dispose();
		}

		private void Record(ArchiveConnectionState state)
		{
			lock (_guard)
			{
				_states.Add(state);

				if (!state.IsConnected)
				{
					return;
				}

				_connectedCount++;

				foreach (var waiter in _waiters.Where(waiter => waiter.Count <= _connectedCount))
				{
					waiter.Reached.TrySetResult();
				}

				_waiters.RemoveAll(waiter => waiter.Count <= _connectedCount);
			}
		}
	}

	// Collects every delivered batch and hands out a gate for the next one. The gate is taken before the row
	// that triggers it is written and awaited after, so no delivery can slip between the two.
	private sealed class BatchCollector : IDisposable
	{
		private readonly List<IReadOnlyList<Sample>> _batches = [];

		private readonly Lock _guard = new();

		private readonly IDisposable _subscription;

		private TaskCompletionSource? _nextBatch;

		public BatchCollector(IObservable<IReadOnlyList<Sample>> batches)
		{
			_subscription = batches.Subscribe(Collect);
		}

		public IReadOnlyList<IReadOnlyList<Sample>> Batches
		{
			get
			{
				lock (_guard)
				{
					return _batches.ToArray();
				}
			}
		}

		public Task NextBatch()
		{
			lock (_guard)
			{
				var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

				_nextBatch = gate;

				return gate.Task;
			}
		}

		public void Dispose()
		{
			_subscription.Dispose();
		}

		private void Collect(IReadOnlyList<Sample> batch)
		{
			lock (_guard)
			{
				_batches.Add(batch);

				_nextBatch?.TrySetResult();
			}
		}
	}
}
