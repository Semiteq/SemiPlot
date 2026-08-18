using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PostgresCompositionTests
{
	private static ServiceProvider BuildProvider()
	{
		return new ServiceCollection()
			.AddPostgresData()
			.BuildServiceProvider();
	}

	[Fact]
	public void AddPostgresDataResolvesThePostgresProvider()
	{
		using var services = BuildProvider();

		var provider = services.GetRequiredService<IDataProvider>();

		Assert.IsType<PostgresDataProvider>(provider);
	}

	// The lifetime is read off the descriptor rather than inferred from resolving twice: a Scoped
	// registration also returns the same instance twice from a root provider, so Assert.Same cannot tell
	// the two apart.
	[Theory]
	[InlineData(typeof(IDataProvider))]
	[InlineData(typeof(IScheduler))]
	public void AddPostgresDataRegistersASingleton(Type serviceType)
	{
		var collection = new ServiceCollection().AddPostgresData();

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
	public void AddPostgresDataReturnsTheSameCollection()
	{
		var collection = new ServiceCollection();

		var returned = collection.AddPostgresData();

		Assert.Same(collection, returned);
	}

	[Fact]
	public async Task QueryPensAsyncFailsWithTheNotImplementedError()
	{
		var provider = new PostgresDataProvider();

		var result = await provider.QueryPensAsync();

		var error = Assert.Single(result.Errors.OfType<ProviderNotImplementedError>());
		Assert.True(result.IsFailed);
		Assert.Equal(nameof(IDataProvider.QueryPensAsync), error.MemberName);
	}

	[Fact]
	public async Task QueryHistoryAsyncFailsWithTheNotImplementedError()
	{
		var provider = new PostgresDataProvider();

		var result = await provider.QueryHistoryAsync(
			[1L],
			new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
			AggregationLayer.Raw,
			100);

		var error = Assert.Single(result.Errors.OfType<ProviderNotImplementedError>());
		Assert.True(result.IsFailed);
		Assert.Equal(nameof(IDataProvider.QueryHistoryAsync), error.MemberName);
	}

	[Fact]
	public async Task QueryArchiveExtentAsyncFailsWithTheNotImplementedError()
	{
		var provider = new PostgresDataProvider();

		var result = await provider.QueryArchiveExtentAsync();

		var error = Assert.Single(result.Errors.OfType<ProviderNotImplementedError>());
		Assert.True(result.IsFailed);
		Assert.Equal(nameof(IDataProvider.QueryArchiveExtentAsync), error.MemberName);
	}

	[Fact]
	public async Task SubscribeCompletesImmediately()
	{
		var provider = new PostgresDataProvider();

		var samples = await provider.Subscribe([1L]).ToArray();

		Assert.Empty(samples);
	}
}
