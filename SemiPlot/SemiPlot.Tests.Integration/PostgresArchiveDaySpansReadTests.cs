using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class PostgresArchiveDaySpansReadTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	private static readonly string _appendRowCommand = $"""
		INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, @l, @t, 1, {ArchiveRow.OrdinaryQuality});
		""";

	private static readonly Lazy<ArchiveDaySpan[]> _seededSpans = new(GenerateSeededSpans);

	private static DateOnly FirstSeededDay => DateOnly.FromDateTime(ArchiveTemplate.Slice.Start);

	private static DateOnly LastSeededDay => DateOnly.FromDateTime(ArchiveTemplate.Slice.End).AddDays(-1);

	[Fact]
	public async Task ARangeAroundTheSeed_ReturnsTheRawBoundsOfEachSeededDayInUtc()
	{
		var result = await ReadSpansAsync(
			seededArchive.Database.PlotConnectionString,
			FirstSeededDay.AddDays(-3),
			LastSeededDay.AddDays(3));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().NotBeEmpty();
		result.Value.Should().Equal(_seededSpans.Value);
		result.Value.Select(span => span.FirstUtc.Kind).Should().AllBeEquivalentTo(DateTimeKind.Utc);
	}

	[Fact]
	public async Task ARangeBeforeTheSeed_ReturnsNoSpan()
	{
		var result = await ReadSpansAsync(
			seededArchive.Database.PlotConnectionString,
			FirstSeededDay.AddDays(-30),
			FirstSeededDay.AddDays(-1));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEmpty();
	}

	// The raw day holds one row, so its span opens and closes at that row, converted out of the archive's zone.
	[Fact]
	public async Task ADayHoldingOnlyACoarseRow_IsAbsentBetweenTwoDaysWithRawRows()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var penId = RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)[0].PenId;
		var coarseDay = LastSeededDay.AddDays(1);
		var rawDay = LastSeededDay.AddDays(2);
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Template,
			async clone =>
			{
				await AppendRowAsync(clone, penId, LayerThinner.MinuteLayer, Noon(coarseDay), cancellationToken);
				await AppendRowAsync(clone, penId, ArchiveRow.RawLayer, Noon(rawDay), cancellationToken);
			},
			cancellationToken);

		var result = await ReadSpansAsync(database.PlotConnectionString, FirstSeededDay, rawDay.AddDays(1));

		var rawNoonUtc = ArchiveProviderFactory.TimeConverter.ToUtc(Noon(rawDay));

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().Equal([.. _seededSpans.Value, new ArchiveDaySpan(rawDay, rawNoonUtc, rawNoonUtc)]);
	}

	[Fact]
	public async Task AnEmptiedCatalogue_ReturnsNoSpan()
	{
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Template,
			clone => ArchiveDatabase.ExecuteAsync(
				clone.AdminConnectionString,
				ArchiveReadSupport.EmptyCatalogCommand,
				TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);

		var result = await ReadSpansAsync(database.PlotConnectionString, FirstSeededDay, LastSeededDay);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.Should().BeEmpty();
	}

	[Fact]
	public async Task ADroppedTrendsTable_FailsNamingEveryRelationTheReadTouches()
	{
		await using var database = await postgresContainerFixture.CloneAsync(
			CloneSource.Provisioned,
			clone => ArchiveDatabase.ExecuteAsync(
				clone.WriterConnectionString,
				ArchiveReadSupport.DropTrendsCommand,
				TestContext.Current.CancellationToken),
			TestContext.Current.CancellationToken);

		var result = await ReadSpansAsync(database.PlotConnectionString, FirstSeededDay, LastSeededDay);

		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("semiplot_tags, trends");
		error.Database.Should().Be(database.Name);
	}

	[Fact]
	public async Task ACancelledRead_ThrowsInsteadOfFailing()
	{
		await using var services = ArchiveProviderFactory.Build(seededArchive.Database.PlotConnectionString);
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		var read = () => services.GetRequiredService<IDataProvider>()
			.QueryArchiveDaySpansAsync(FirstSeededDay, LastSeededDay, cancellation.Token);

		await read.Should().ThrowAsync<OperationCanceledException>();
	}

	private static async Task<Result<IReadOnlyList<ArchiveDaySpan>>> ReadSpansAsync(
		string connectionString,
		DateOnly firstDay,
		DateOnly lastDay)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		return await services.GetRequiredService<IDataProvider>()
			.QueryArchiveDaySpansAsync(firstDay, lastDay, TestContext.Current.CancellationToken);
	}

	// The day partition comes first: a row with no matching partition lands in the default one.
	private static async Task AppendRowAsync(
		ArchiveDatabase database,
		int penId,
		short layer,
		DateTime archiveLocal,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var partition = new NpgsqlCommand(PartitionScript.CreateStatement(archiveLocal.Date), connection);

		await partition.ExecuteNonQueryAsync(cancellationToken);

		await using var command = new NpgsqlCommand(_appendRowCommand, connection);

		command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = penId });
		command.Parameters.Add(new NpgsqlParameter("l", NpgsqlDbType.Smallint) { Value = layer });
		command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Timestamp) { Value = archiveLocal });

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static DateTime Noon(DateOnly day)
	{
		return day.ToDateTime(new TimeOnly(12, 0));
	}

	// The seeder's timestamps are the archive's own wall clock, so a row's day is its date as written; the
	// bounds leave the provider through the same zone the factory hands it.
	private static ArchiveDaySpan[] GenerateSeededSpans()
	{
		return [.. RawLayerGenerator.Generate(ArchiveTemplate.Slice)
			.Where(row => row.Layer == ArchiveRow.RawLayer)
			.GroupBy(row => DateOnly.FromDateTime(row.Timestamp))
			.OrderBy(day => day.Key)
			.Select(day => new ArchiveDaySpan(
				day.Key,
				ArchiveProviderFactory.TimeConverter.ToUtc(day.Min(row => row.Timestamp)),
				ArchiveProviderFactory.TimeConverter.ToUtc(day.Max(row => row.Timestamp))))];
	}
}
