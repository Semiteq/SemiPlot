using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Every synchronisation point here is an awaited emission (Connected, or a delivered batch), never a
// timeout. Appending before the baseline has run races the subscription and hangs the await.
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
		await Writer().WriteAsync(SeededRows(), _day, _nextDay);
	}

	// The second subscription is what proves the elapsed time: its own delivery cannot happen before a poll
	// interval has passed since the first was dropped.
	[Fact]
	public async Task DisposingASubscriptionStopsItsPoll()
	{
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

		armed.NoFaultWasSeen.Should().BeTrue(
			"an armed point counted off the shared stream is the right subscription's only while no fault "
			+ "has been raised and cleared behind it");
		first.Batches.Count.Should().Be(deliveredBeforeDisposal);
	}

	// The sequence is cold, so each subscription runs a RealtimePoll of its own and takes a baseline of its
	// own. The second one's baseline is already past the row the first was delivered, so that row must never
	// reach it, while the row appended after both are armed reaches both.
	[Fact]
	public async Task TwoSubscriptionsEachKeepTheirOwnLastSeen()
	{
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

		armed.NoFaultWasSeen.Should().BeTrue(
			"the second subscription's armed point is the second connected state only while no fault has "
			+ "been raised and cleared behind it");
		Delivered(first).Should().Equal(
			[_timeConverter.ToUtc(beforeTheSecondSubscription), _timeConverter.ToUtc(afterBoth)]);
		Delivered(second).Should().Equal([_timeConverter.ToUtc(afterBoth)]);
	}

	// The armed point is per subscription, not per provider: a consumer subscribing to a provider that has
	// already been polling for hours still has an event to sequence on. Nothing here appends, so the only
	// states the stream can carry are the two armed points.
	[Fact]
	public async Task EverySubscriptionReportsConnectedOnItsOwnFirstTick()
	{
		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		using var armed = new ArmedGate(provider.ConnectionFaults);
		using var first = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(1);

		using var second = new BatchCollector(provider.Subscribe(_subscribedPenIds));

		await armed.Armed(2);

		armed.States.Count.Should().Be(2);
		armed.States.Should().AllSatisfy(state => state.IsConnected.Should().BeTrue());
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

	// The nth Connected is the nth subscription's armed point, and NoFaultWasSeen is what makes that an
	// asserted precondition instead of an assumption. The waiters run their continuations asynchronously
	// (CLAUDE.md, "An xunit v3 test project is an executable").
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

		/// <summary>Completes once the stream has carried the given count of connected states.</summary>
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
