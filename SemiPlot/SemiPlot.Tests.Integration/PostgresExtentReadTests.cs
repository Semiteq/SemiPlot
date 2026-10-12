using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
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
		var result = await ReadExtentAsync(seededArchive.Database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.IsEmpty.Should().BeFalse();
		result.Value.Should().Be(_seededExtent.Value);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AnEmptiedCatalogueOrTrendsTableYieldsAnEmptyExtent(bool emptyTheCatalogue)
	{
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Template,
			clone => ArchiveDatabase.ExecuteAsync(
				clone.AdminConnectionString,
				emptyTheCatalogue ? ArchiveReadSupport.EmptyCatalogCommand : EmptyTrendsCommand,
				TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.PlotConnectionString);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().Be(ArchiveExtent.Empty);
	}

	// The extent statement touches both relations, so either one missing names both: reporting trends
	// alone would send the operator to a table that is still there.
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task ADroppedRelationFailsNamingEveryRelationTheReadTouches(bool dropTheCatalogue)
	{
		await using var database = await postgresContainerFixture.CloneAsync(
			dropTheCatalogue ? CloneSource.Template : CloneSource.Provisioned,
			clone => ArchiveDatabase.ExecuteAsync(
				dropTheCatalogue ? clone.AdminConnectionString : clone.WriterConnectionString,
				dropTheCatalogue ? ArchiveReadSupport.DropCatalogCommand : ArchiveReadSupport.DropTrendsCommand,
				TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);

		var result = await ReadExtentAsync(database.PlotConnectionString);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("semiplot_tags, trends");
		error.Database.Should().Be(database.Name);
	}

	private static async Task<Result<ArchiveExtent>> ReadExtentAsync(string connectionString)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		return await services.GetRequiredService<IDataProvider>().QueryArchiveExtentAsync();
	}

	private static ArchiveExtent GenerateSeededExtent()
	{
		var timestamps = RawLayerGenerator.Generate(ArchiveTemplate.Slice)
			.Where(row => row.Layer == ArchiveRow.RawLayer)
			.Select(row => row.Timestamp)
			.ToArray();

		return new ArchiveExtent(
			ArchiveProviderFactory.TimeConverter.ToUtc(timestamps.Min()),
			ArchiveProviderFactory.TimeConverter.ToUtc(timestamps.Max()));
	}
}
