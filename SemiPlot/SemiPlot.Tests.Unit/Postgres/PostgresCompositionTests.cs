using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

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
	private static PostgresDataProvider NewProvider(PostgresConnectionSettings settings, NpgsqlDataSource dataSource)
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

		provider.Should().BeOfType<PostgresDataProvider>();
	}

	[Fact]
	public void AddPostgresDataResolvesTheDataSource()
	{
		using var services = BuildProvider();

		services.GetRequiredService<NpgsqlDataSource>().Should().NotBeNull();
	}

	// The data source is a DI singleton, and ServiceProvider.Dispose() throws for an instantiated
	// singleton implementing only IAsyncDisposable. Resolving one and then disposing synchronously is the
	// case that catches an async-only wrapper.
	[Fact]
	public void TheResolvedDataSourceSurvivesASynchronousProviderDispose()
	{
		var services = BuildProvider();

		_ = services.GetRequiredService<NpgsqlDataSource>();

		services.Dispose();
	}

	[Fact]
	public void AddPostgresDataResolvesTheExceptionMapper()
	{
		using var services = BuildProvider();

		services.GetRequiredService<ArchiveExceptionMapper>().Should().NotBeNull();
	}

	[Fact]
	public void AddPostgresDataResolvesTheTimeConverter()
	{
		using var services = BuildProvider();

		services.GetRequiredService<ArchiveTimeConverter>().Should().NotBeNull();
	}

	// ArchiveTimeConverter exposes no zone member, so the registration is asserted through the behaviour
	// the zone determines: a winter wall-clock reading under Europe/Berlin sits one hour ahead of UTC.
	[Fact]
	public void TheResolvedConverterCarriesTheConfiguredSourceZone()
	{
		using var services = BuildProvider();
		var archiveLocal = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = services.GetRequiredService<ArchiveTimeConverter>().ToUtc(archiveLocal);

		utc.Should().Be(archiveLocal - _sourceZone.GetUtcOffset(archiveLocal));
		(archiveLocal - utc).Should().Be(TimeSpan.FromHours(1));
	}

	// The lifetime is read off the descriptor rather than inferred from resolving twice: a Scoped
	// registration also returns the same instance twice from a root provider, so a same-reference check
	// cannot tell the two apart.
	[Theory]
	[InlineData(typeof(IDataProvider))]
	[InlineData(typeof(IScheduler))]
	[InlineData(typeof(NpgsqlDataSource))]
	[InlineData(typeof(ArchiveExceptionMapper))]
	[InlineData(typeof(ArchiveTimeConverter))]
	[InlineData(typeof(PostgresConnectionSettings))]
	public void AddPostgresDataRegistersASingleton(Type serviceType)
	{
		var collection = BuildCollection();

		var descriptor = collection.Should().ContainSingle(service => service.ServiceType == serviceType).Which;

		descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
	}

	[Fact]
	public void AddPostgresDataResolvesAScheduler()
	{
		using var services = BuildProvider();

		services.GetRequiredService<IScheduler>().Should().NotBeNull();
	}

	[Fact]
	public void AddPostgresDataResolvesTheSettingsItWasGiven()
	{
		var settings = Settings();
		using var services = new ServiceCollection()
			.AddLogging()
			.AddPostgresData(settings)
			.BuildServiceProvider();

		services.GetRequiredService<PostgresConnectionSettings>().Should().BeSameAs(settings);
	}

	// The non-null penIds precondition is the interface's, not one member's, so it throws here exactly as
	// the query members do rather than answering a null list with an empty sequence.
	[Fact]
	public void SubscribeRejectsANullPenList()
	{
		var settings = Settings();
		using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);
		var provider = NewProvider(settings, dataSource);

		Action act = () => provider.Subscribe(null!);

		act.Should().Throw<ArgumentNullException>();
	}

	// Asserts that dropping a subscription at once returns without blocking and leaves no loop running.
	// Silence alone would not prove that (a leaked loop against this unreachable address is also silent),
	// so a second, left-running provider's own ArchiveFault.ConnectionLost calibrates the wait instead.
	[Fact]
	public async Task SubscribingAndDisposingAtOnceDeliversNothingAndStopsThePoll()
	{
		var settings = Settings(TimeSpan.FromMilliseconds(10));

		using var droppedDataSource = NpgsqlDataSource.Create(settings.ConnectionString);
		using var runningDataSource = NpgsqlDataSource.Create(settings.ConnectionString);

		var dropped = NewProvider(settings, droppedDataSource);
		var running = NewProvider(settings, runningDataSource);

		var droppedBatches = new List<IReadOnlyList<Sample>>();
		var droppedStates = new List<ArchiveConnectionState>();
		var runningFaulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var droppedWatch = dropped.ConnectionFaults.Subscribe(droppedStates.Add);
		using var runningWatch = running.ConnectionFaults.Subscribe(_ => runningFaulted.TrySetResult());

		dropped.Subscribe([1]).Subscribe(droppedBatches.Add).Dispose();

		using var control = running.Subscribe([1]).Subscribe(_ => { });

		await runningFaulted.Task.WaitAsync(TestContext.Current.CancellationToken);

		// The control needed three ticks to get here, so a loop that survived its own disposal has had the
		// same three plus this margin. Anything it raised has been recorded by now.
		await Task.Delay(settings.PollInterval * 10, TestContext.Current.CancellationToken);

		droppedBatches.Should().BeEmpty();
		droppedStates.Should().BeEmpty();
	}
}
