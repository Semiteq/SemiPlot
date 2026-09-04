using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

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
		var result = await ReadExtentAsync(seededArchive.Database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.IsEmpty.Should().BeFalse();
		result.Value.Should().Be(_seededExtent.Value);
	}

	[Fact]
	public async Task AnEmptiedCatalogueYieldsAnEmptyExtent()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.EmptyCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.IsEmpty.Should().BeTrue();
		result.Value.Should().Be(ArchiveExtent.Empty);
	}

	[Fact]
	public async Task AnEmptyTrendsTableYieldsAnEmptyExtent()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			EmptyTrendsCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.IsEmpty.Should().BeTrue();
		result.Value.Should().Be(ArchiveExtent.Empty);
	}

	// The extent statement touches both relations and reports its own, so a dropped catalogue is reported
	// as a missing trends.
	[Fact]
	public async Task ADroppedCatalogueFailsNamingTheStatementsOwnRelation()
	{
		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			ArchiveReadSupport.DropCatalogCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("trends");
		error.Database.Should().Be(database.Name);
	}

	[Fact]
	public async Task ADroppedTrendsTableFailsNamingTrends()
	{
		await using var database = await postgresContainerFixture.CloneProvisionedAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.WriterConnectionString,
			ArchiveReadSupport.DropTrendsCommand,
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.ReaderConnectionString);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("trends");
		error.Database.Should().Be(database.Name);
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
