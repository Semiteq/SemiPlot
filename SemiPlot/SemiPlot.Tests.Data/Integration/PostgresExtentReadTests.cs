using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresExtentReadTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	// TRUNCATE over the partitioned parent empties every partition without a subprocess, and leaves
	// semiplot_tags populated so the statement's outer relation still has rows to join laterally.
	private const string EmptyTrendsCommand = "TRUNCATE public.trends;";

	// The bounds the seeder wrote, generated again rather than read back, so the assertion covers the
	// statement and the outward time conversion instead of comparing the query to itself.
	private static readonly Lazy<ArchiveExtent> _seededExtent = new(GenerateSeededExtent);

	[Fact]
	public async Task TheSeededExtentMatchesTheSeedersFirstAndLastTimestamps()
	{
		postgresContainerFixture.RequireAvailable();

		var result = await ReadExtentAsync(seededArchive.Database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.False(result.Value.IsEmpty);
		Assert.Equal(_seededExtent.Value, result.Value);
	}

	[Fact]
	public async Task AnEmptiedCatalogueYieldsAnEmptyExtent()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.EmptyCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.True(result.Value.IsEmpty);
		Assert.Equal(ArchiveExtent.Empty, result.Value);
	}

	[Fact]
	public async Task AnEmptyTrendsTableYieldsAnEmptyExtent()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			EmptyTrendsCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		Assert.True(result.IsSuccess, ArchiveReadSupport.Describe(result));
		Assert.True(result.Value.IsEmpty);
		Assert.Equal(ArchiveExtent.Empty, result.Value);
	}

	// The extent statement touches both relations and reports its own, so a dropped catalogue is reported
	// as a missing trends. That name reaches a log line and nothing further, and both tables carry the same
	// remedy. This is the only coverage of the extent read's 42P01 path.
	[Fact]
	public async Task ADroppedCatalogueFailsNamingTheStatementsOwnRelation()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.DropCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		Assert.True(result.IsFailed);

		var error = Assert.Single(result.Errors.OfType<ArchiveError>());

		Assert.Equal(ArchiveFault.TableMissing, error.Kind);
		Assert.Equal("trends", error.Detail);
		Assert.Equal(database.Name, error.Database);
	}

	[Fact]
	public async Task ADroppedTrendsTableFailsNamingTrends()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneProvisionedAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.WriterConnectionString,
			ArchiveReadSupport.DropTrendsCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		Assert.True(result.IsFailed);

		var error = Assert.Single(result.Errors.OfType<ArchiveError>());

		Assert.Equal(ArchiveFault.TableMissing, error.Kind);
		Assert.Equal("trends", error.Detail);
		Assert.Equal(database.Name, error.Database);
	}

	private static async Task<Result<ArchiveExtent>> ReadExtentAsync(string connectionString)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		return await services.GetRequiredService<IDataProvider>().QueryArchiveExtentAsync();
	}

	private static ArchiveExtent GenerateSeededExtent()
	{
		var converter = new ArchiveTimeConverter(ArchiveProviderFactory.SourceTimeZone);

		var timestamps = RawLayerGenerator.Generate(ArchiveTemplate.Slice)
			.Where(row => row.Layer == ArchiveRow.RawLayer)
			.Select(row => row.Timestamp)
			.ToArray();

		return new ArchiveExtent(converter.ToUtc(timestamps.Min()), converter.ToUtc(timestamps.Max()));
	}
}
