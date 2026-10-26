using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// A failed tick and its logged run of failures. No server: every tick connects to an address nothing answers.
// What a successful tick reads is covered by RealtimePollReadTests.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimePollTests
{
	private const int TickCount = 5;

	private static readonly PostgresConnectionSettings _settings = UnreachableArchive.Settings(TimeSpan.FromSeconds(1));

	private static readonly PostgresConnectionSettings _fastSettings =
		UnreachableArchive.Settings(TimeSpan.FromMilliseconds(10));

	[Fact]
	public async Task AFailedTickEmitsNoSample()
	{
		await using var services = UnreachableArchive.Services(_settings);

		var samples = await NewPoll(services).ReadOnceAsync(TestContext.Current.CancellationToken);

		samples.Should().BeEmpty();
	}

	[Fact]
	public async Task EachFailedTickLogsTheRunOfFailuresItExtends()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await using var services = UnreachableArchive.Services(_settings);
		using var logs = new FakeLoggerProvider();

		var poll = NewPoll(services, logs.CreateLogger("RealtimePoll"));

		for (var tick = 0; tick < TickCount; tick++)
		{
			await poll.ReadOnceAsync(cancellationToken);
		}

		logs.Collector.GetSnapshot()
			.Select(entry => entry.GetStructuredStateValue(ArmedGate.FailureRunKey))
			.Should().Equal("1", "2", "3", "4", "5");
	}

	[Fact]
	public async Task ASubscriptionThatOnlyFailsNeitherCompletesNorFaults()
	{
		// The last failure runs only if nothing terminal followed the one before it.
		const int FailedTicksBeforeAssert = 6;

		using var gate = new ArmedGate();
		await using var services = UnreachableArchive.Services(_fastSettings, gate);

		var liveSource = services.GetRequiredService<ILiveSource>();

		using var notifications = new BatchCollector<Notification<IReadOnlyList<Sample>>>(
			liveSource.Subscribe([1]).Materialize());

		await gate.FailuresReached(FailedTicksBeforeAssert)
			.WaitAsync(ClonedArchiveTest.GateBound, TestContext.Current.CancellationToken);

		notifications.Batches.Should().BeEmpty();
	}

	[Fact]
	public async Task ThePollLogsUnderTheLiveSourcesCategory()
	{
		using var logs = new FakeLoggerProvider();
		using var gate = new ArmedGate(logs);
		await using var services = UnreachableArchive.Services(_fastSettings, gate);

		using var subscription = services.GetRequiredService<ILiveSource>().Subscribe([1]).Subscribe(_ => { });

		// The gate counts a line before its inner logger writes it, so the second failure proves the first written.
		await gate.FailuresReached(2).WaitAsync(ClonedArchiveTest.GateBound, TestContext.Current.CancellationToken);

		logs.Collector.GetSnapshot()
			.Where(entry => entry.GetStructuredStateValue(ArmedGate.FailureRunKey) is not null)
			.Should().NotBeEmpty()
			.And.OnlyContain(entry => entry.Category == typeof(PostgresLiveSource).FullName);
	}

	[Fact]
	public async Task ThePollWaitsTheConfiguredIntervalAfterATick()
	{
		var pollInterval = TimeSpan.FromMilliseconds(1234);
		var scheduler = new DueTimeRecorder();
		await using var services = UnreachableArchive.Services(
			UnreachableArchive.Settings(pollInterval),
			scheduler: scheduler);

		using var subscription = services.GetRequiredService<ILiveSource>().Subscribe([1]).Subscribe(_ => { });

		var dueTime = await scheduler.FirstDueTime
			.WaitAsync(ClonedArchiveTest.GateBound, TestContext.Current.CancellationToken);

		dueTime.Should().Be(pollInterval);
	}

	private sealed class DueTimeRecorder : IScheduler
	{
		private readonly TaskCompletionSource<TimeSpan> _firstDueTime =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task<TimeSpan> FirstDueTime => _firstDueTime.Task;

		public DateTimeOffset Now => DefaultScheduler.Instance.Now;

		public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
		{
			return DefaultScheduler.Instance.Schedule(state, action);
		}

		public IDisposable Schedule<TState>(
			TState state,
			TimeSpan dueTime,
			Func<IScheduler, TState, IDisposable> action)
		{
			_firstDueTime.TrySetResult(dueTime);

			return DefaultScheduler.Instance.Schedule(state, dueTime, action);
		}

		public IDisposable Schedule<TState>(
			TState state,
			DateTimeOffset dueTime,
			Func<IScheduler, TState, IDisposable> action)
		{
			return DefaultScheduler.Instance.Schedule(state, dueTime, action);
		}
	}
}
