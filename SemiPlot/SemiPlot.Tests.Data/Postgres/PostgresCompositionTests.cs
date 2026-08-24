using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data;
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

	private static PostgresConnectionSettings Settings()
	{
		return ConnectionSettingsFactory.Create(_sourceZone);
	}

	// The caller owns the data source, because it holds a connection pool and its idle timer.
	private static PostgresDataProvider NewProvider(PostgresConnectionSettings settings, ArchiveDataSource dataSource)
	{
		return new PostgresDataProvider(
			dataSource,
			new ArchiveTimeConverter(settings.SourceTimeZone),
			new ArchiveExceptionMapper(settings),
			new StatementTimeoutReader(dataSource, NullLogger<StatementTimeoutReader>.Instance),
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
	public void AddPostgresDataResolvesTheStatementTimeoutReader()
	{
		using var services = BuildProvider();

		Assert.NotNull(services.GetRequiredService<StatementTimeoutReader>());
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
	[InlineData(typeof(StatementTimeoutReader))]
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

	// The non-null penIds precondition is the interface's, not one member's: RandomStubDataProvider
	// throws here too, so an empty sequence for a null list would split the two implementations.
	[Fact]
	public void SubscribeRejectsANullPenList()
	{
		var settings = Settings();
		using var dataSource = new ArchiveDataSource(settings);
		var provider = NewProvider(settings, dataSource);

		Assert.Throws<ArgumentNullException>(() => provider.Subscribe(null!));
	}

	[Fact]
	public async Task SubscribeCompletesImmediately()
	{
		var settings = Settings();
		using var dataSource = new ArchiveDataSource(settings);
		var provider = NewProvider(settings, dataSource);

		var samples = await provider.Subscribe([1L]).ToArray();

		Assert.Empty(samples);
	}
}
