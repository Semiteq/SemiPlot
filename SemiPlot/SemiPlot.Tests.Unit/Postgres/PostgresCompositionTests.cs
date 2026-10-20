using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
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

	private static PostgresConnectionSettings Settings()
	{
		return ConnectionSettingsFactory.Create(_sourceZone);
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
	public void AddPostgresDataResolvesThePostgresLiveSource()
	{
		using var services = BuildProvider();

		var liveSource = services.GetRequiredService<ILiveSource>();

		liveSource.Should().BeOfType<PostgresLiveSource>();
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

	// ArchiveTimeConverter exposes no zone member, so the registration is asserted through the behaviour
	// the zone determines: a winter wall-clock reading under Europe/Berlin sits one hour ahead of UTC.
	[Fact]
	public void TheResolvedConverterCarriesTheConfiguredSourceZone()
	{
		using var services = BuildProvider();
		var archiveLocal = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified);

		var utc = services.GetRequiredService<ArchiveTimeConverter>().ToUtc(archiveLocal);

		(archiveLocal - utc).Should().Be(TimeSpan.FromHours(1));
	}

	// The lifetime is read off the descriptor rather than inferred from resolving twice: a Scoped
	// registration also returns the same instance twice from a root provider, so a same-reference check
	// cannot tell the two apart.
	[Theory]
	[InlineData(typeof(IDataProvider))]
	[InlineData(typeof(ILiveSource))]
	[InlineData(typeof(IPenCatalogueEditor))]
	[InlineData(typeof(IMarkerStore))]
	[InlineData(typeof(IScheduler))]
	[InlineData(typeof(TimeProvider))]
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
	public void AddPostgresDataResolvesTheSystemClock()
	{
		using var services = BuildProvider();

		services.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
	}

	[Fact]
	public void AClockRegisteredBeforeAddPostgresDataIsTheOneResolved()
	{
		var clock = new FixedClock(new DateTimeOffset(2026, 1, 1, 9, 5, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
		using var services = new ServiceCollection()
			.AddLogging()
			.AddSingleton<TimeProvider>(clock)
			.AddPostgresData(Settings())
			.BuildServiceProvider();

		services.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
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
}
