using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PostgresCompositionTests
{
	private static readonly TimeZoneInfo _sourceZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	private static PostgresConnectionSettings Settings(TimeSpan? pollInterval = null)
	{
		return ConnectionSettingsFactory.Create(_sourceZone, pollInterval: pollInterval);
	}

	// The caller owns the data source, because it holds a connection pool and its idle timer.
	private static PostgresDataProvider NewProvider(PostgresConnectionSettings settings, ArchiveDataSource dataSource)
	{
		return new PostgresDataProvider(
			dataSource,
			new ArchiveTimeConverter(settings.SourceTimeZone),
			new ArchiveExceptionMapper(settings),
			settings,
			DefaultScheduler.Instance,
			NullLogger<PostgresDataProvider>.Instance);
	}

	private static ServiceCollection BuildCollection()
	{
		var collection = new ServiceCollection();

		collection.AddLogging();
		collection.AddPostgresData(Settings());

		return collection;
	}

	private static ServiceProvider BuildProvider()
	{
		return BuildCollection().BuildServiceProvider();
	}

	[Fact]
	public void AddPostgresDataResolvesThePostgresProvider()
	{
		using var services = BuildProvider();

		var provider = services.GetRequiredService<IDataProvider>();

		Assert.IsType<PostgresDataProvider>(provider);
	}

	[Fact]
	public void AddPostgresDataResolvesTheDataSource()
	{
		using var services = BuildProvider();

		Assert.NotNull(services.GetRequiredService<ArchiveDataSource>());
	}

	// The data source is a DI singleton, and ServiceProvider.Dispose() throws for an instantiated
	// singleton implementing only IAsyncDisposable. Resolving one and then disposing synchronously is the
	// case that catches an async-only wrapper.
	[Fact]
	public void TheResolvedDataSourceSurvivesASynchronousProviderDispose()
	{
		var services = BuildProvider();

		_ = services.GetRequiredService<ArchiveDataSource>();

		services.Dispose();
	}

	[Fact]
	public void AddPostgresDataResolvesTheExceptionMapper()
	{
		using var services = BuildProvider();

		Assert.NotNull(services.GetRequiredService<ArchiveExceptionMapper>());
	}

	[Fact]
	public void AddPostgresDataResolvesTheTimeConverter()
	{
		using var services = BuildProvider();

		Assert.NotNull(services.GetRequiredService<ArchiveTimeConverter>());
	}

	// ArchiveTimeConverter exposes no zone member, so the registration is asserted through the behaviour
	// the zone determines: a winter wall-clock reading under Europe/Berlin sits one hour ahead of UTC.
	[Fact]
	public void TheResolvedConverterCarriesTheConfiguredSourceZone()
	{
		using var services = BuildProvider();
		var archiveLocal = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = services.GetRequiredService<ArchiveTimeConverter>().ToUtc(archiveLocal);

		Assert.Equal(archiveLocal - _sourceZone.GetUtcOffset(archiveLocal), utc);
		Assert.Equal(TimeSpan.FromHours(1), archiveLocal - utc);
	}

	// The lifetime is read off the descriptor rather than inferred from resolving twice: a Scoped
	// registration also returns the same instance twice from a root provider, so Assert.Same cannot tell
	// the two apart.
	[Theory]
	[InlineData(typeof(IDataProvider))]
	[InlineData(typeof(IScheduler))]
	[InlineData(typeof(ArchiveDataSource))]
	[InlineData(typeof(ArchiveExceptionMapper))]
	[InlineData(typeof(ArchiveTimeConverter))]
	[InlineData(typeof(PostgresConnectionSettings))]
	public void AddPostgresDataRegistersASingleton(Type serviceType)
	{
		var collection = BuildCollection();

		var descriptor = Assert.Single(collection, service => service.ServiceType == serviceType);

		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
	}

	[Fact]
	public void AddPostgresDataResolvesAScheduler()
	{
		using var services = BuildProvider();

		Assert.NotNull(services.GetRequiredService<IScheduler>());
	}

	[Fact]
	public void AddPostgresDataResolvesTheSettingsItWasGiven()
	{
		var settings = Settings();
		using var services = new ServiceCollection()
			.AddLogging()
			.AddPostgresData(settings)
			.BuildServiceProvider();

		Assert.Same(settings, services.GetRequiredService<PostgresConnectionSettings>());
	}

	// The non-null penIds precondition is the interface's, not one member's, so it throws here exactly as
	// the query members do rather than answering a null list with an empty sequence.
	[Fact]
	public void SubscribeRejectsANullPenList()
	{
		var settings = Settings();
		using var dataSource = new ArchiveDataSource(settings);
		var provider = NewProvider(settings, dataSource);

		Assert.Throws<ArgumentNullException>(() => provider.Subscribe(null!));
	}

	// A subscription no longer completes, so nothing may await the end of the sequence. The property that
	// replaces the old completion assertion is that taking a subscription and dropping it at once returns
	// rather than blocking and leaves no loop running behind it.
	//
	// Silence alone would prove nothing here — these settings point at an address nothing answers, so a
	// leaked loop emits no sample either. What it does emit is a fault: three refused ticks raise
	// ArchiveFault.ConnectionLost on the provider's own connection stream. The control is a second provider
	// whose subscription is left running, and the test waits for its fault before reading the dropped one's
	// silence, so the wait is calibrated by a loop that really is polling rather than by a guessed delay.
	//
	// The disposal of a loop already in flight against a live server is
	// RealtimeSubscriptionTests.DisposingASubscriptionStopsItsPoll; this one guards the composition — a
	// Subscribe body that started the loop and returned a disposable not wired to it.
	[Fact]
	public async Task SubscribingAndDisposingAtOnceDeliversNothingAndStopsThePoll()
	{
		var settings = Settings(TimeSpan.FromMilliseconds(10));

		using var droppedDataSource = new ArchiveDataSource(settings);
		using var runningDataSource = new ArchiveDataSource(settings);

		var dropped = NewProvider(settings, droppedDataSource);
		var running = NewProvider(settings, runningDataSource);

		var droppedBatches = new List<IReadOnlyList<Sample>>();
		var droppedStates = new List<ArchiveConnectionState>();
		var runningFaulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var droppedWatch = dropped.ConnectionFaults.Subscribe(droppedStates.Add);
		using var runningWatch = running.ConnectionFaults.Subscribe(_ => runningFaulted.TrySetResult());

		dropped.Subscribe([1L]).Subscribe(droppedBatches.Add).Dispose();

		using var control = running.Subscribe([1L]).Subscribe(_ => { });

		await runningFaulted.Task.WaitAsync(TestContext.Current.CancellationToken);

		// The control needed three ticks to get here, so a loop that survived its own disposal has had the
		// same three plus this margin. Anything it raised has been recorded by now.
		await Task.Delay(settings.PollInterval * 10, TestContext.Current.CancellationToken);

		Assert.Empty(droppedBatches);
		Assert.Empty(droppedStates);
	}

	// The identifiers narrow to the archive's own int4 column, and Subscribe has no Result channel to report
	// one that does not fit — so it throws rather than selecting a different variable's rows.
	[Fact]
	public void SubscribeRejectsAPenIdentifierTheArchiveColumnCannotCarry()
	{
		var settings = Settings();
		using var dataSource = new ArchiveDataSource(settings);
		var provider = NewProvider(settings, dataSource);

		Assert.Throws<ArgumentOutOfRangeException>(() => provider.Subscribe([(long)int.MaxValue + 1]));
	}
}
