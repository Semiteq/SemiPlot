using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Export;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveRowReaderTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int SteadyPenId = 1;
	private const int SeedOnlyPenId = 2;
	private const int StalePenId = 3;
	private const int BoundaryPenId = 4;

	private static readonly DateTime _from = Day.AddHours(1);
	private static readonly DateTime _to = Day.AddHours(2);

	private static readonly DateTime _autumnFrom = new(2026, 10, 25, 1, 0, 0, DateTimeKind.Unspecified);
	private static readonly DateTime _autumnTo = new(2026, 10, 25, 4, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime[] _autumnTimestamps =
	[
		new(2026, 10, 25, 1, 30, 0, DateTimeKind.Unspecified),
		new(2026, 10, 25, 2, 0, 0, DateTimeKind.Unspecified),
		new(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified),
		new(2026, 10, 25, 2, 59, 59, 999, DateTimeKind.Unspecified),
		new(2026, 10, 25, 3, 0, 0, DateTimeKind.Unspecified)
	];

	protected override async ValueTask SeedAsync()
	{
		await Writer().WriteAsync(SeededRows(), Day.AddDays(-1), NextDay);
	}

	[Fact]
	public async Task APensSeedRowComesFirstThenItsWindowRowsAscending()
	{
		var rows = await ReadAsTheExporterDoesAsync([SteadyPenId]);

		rows.Select(row => row.Time).Should().Equal(
			Day.AddMinutes(50),
			Day.AddMinutes(70),
			Day.AddMinutes(80),
			Day.AddMinutes(90));
	}

	[Fact]
	public async Task APenWithNoRowInTheWindowYieldsItsSeedAlone()
	{
		var rows = await ReadAsTheExporterDoesAsync([SeedOnlyPenId]);

		rows.Should().ContainSingle().Which.Time.Should().Be(Day.AddMinutes(40));
	}

	[Fact]
	public async Task ASeedOlderThanOneDayYieldsNothing()
	{
		var rows = await ReadAsTheExporterDoesAsync([StalePenId]);

		rows.Should().BeEmpty();
	}

	[Fact]
	public async Task ASeedExactlyOneDayBeforeTheRangeStartIsReturned()
	{
		var rows = await ReadAsTheExporterDoesAsync([BoundaryPenId]);

		rows.Should().ContainSingle().Which.Time.Should().Be(_from.AddDays(-1));
	}

	[Fact]
	public async Task ARowAtTheRangeEndBelongsToTheNextRange()
	{
		var rows = await ReadAsTheExporterDoesAsync([SteadyPenId]);

		rows.Select(row => row.Time).Should().NotContain(_to);
	}

	[Fact]
	public async Task ASliceEndingAtARowExcludesIt()
	{
		var slice = await ReadSliceAsync([SteadyPenId], _from, Day.AddMinutes(90));

		slice.Select(row => row.Time).Should().Equal(Day.AddMinutes(70), Day.AddMinutes(80));
	}

	[Fact]
	public async Task ACoarseLayerRowIsNeverReturned()
	{
		var rows = await ReadAsTheExporterDoesAsync([SteadyPenId]);

		rows.Select(row => row.Time).Should().NotContain(Day.AddMinutes(75));
	}

	[Fact]
	public async Task EveryRowCarriesThePenItWasReadFor()
	{
		var rows = await ReadAsTheExporterDoesAsync([SteadyPenId]);

		rows.Should().NotBeEmpty().And.OnlyContain(row => row.PenId == SteadyPenId);
	}

	[Fact]
	public async Task TheSeedsComeOutByTimeAndAPenWithNoRecentSeedHasNone()
	{
		var seeds = await ReadSeedsAsync([SteadyPenId, SeedOnlyPenId, StalePenId, BoundaryPenId]);

		seeds.Select(row => row.PenId).Should().Equal(BoundaryPenId, SeedOnlyPenId, SteadyPenId);
	}

	[Fact]
	public async Task TwoSeedsAtOneTimeFollowThePenIdsOrder()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await AppendAsync(Database, SeedOnlyPenId, Day.AddMinutes(55), 1, cancellationToken: cancellationToken);
		await AppendAsync(Database, StalePenId, Day.AddMinutes(55), 1, cancellationToken: cancellationToken);

		(await ReadSeedsAsync([SeedOnlyPenId, StalePenId])).Select(row => row.PenId).Should()
			.Equal(SeedOnlyPenId, StalePenId);
		(await ReadSeedsAsync([StalePenId, SeedOnlyPenId])).Select(row => row.PenId).Should()
			.Equal(StalePenId, SeedOnlyPenId);
	}

	[Fact]
	public async Task ASliceInterleavesPensByTimeAndTiesFollowThePenIdsOrder()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await AppendAsync(Database, SeedOnlyPenId, Day.AddMinutes(70), 1, cancellationToken: cancellationToken);
		await AppendAsync(Database, SeedOnlyPenId, Day.AddMinutes(85), 1, cancellationToken: cancellationToken);

		var forward = await ReadSliceAsync([SteadyPenId, SeedOnlyPenId], _from, _to);
		var reversed = await ReadSliceAsync([SeedOnlyPenId, SteadyPenId], _from, _to);

		forward.Select(row => (row.PenId, row.Time)).Should().Equal(
			(SteadyPenId, Day.AddMinutes(70)),
			(SeedOnlyPenId, Day.AddMinutes(70)),
			(SteadyPenId, Day.AddMinutes(80)),
			(SeedOnlyPenId, Day.AddMinutes(85)),
			(SteadyPenId, Day.AddMinutes(90)));
		reversed.Select(row => (row.PenId, row.Time)).Should().Equal(
			(SeedOnlyPenId, Day.AddMinutes(70)),
			(SteadyPenId, Day.AddMinutes(70)),
			(SteadyPenId, Day.AddMinutes(80)),
			(SeedOnlyPenId, Day.AddMinutes(85)),
			(SteadyPenId, Day.AddMinutes(90)));
	}

	[Fact]
	public async Task ANullValueComesOutAsNull()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await AppendAsync(Database, SeedOnlyPenId, _from.AddMinutes(5), null, cancellationToken: cancellationToken);

		var rows = await ReadAsTheExporterDoesAsync([SeedOnlyPenId]);

		rows.Select(row => row.Value).Should().Equal(SeedOnlyPenId, null);
	}

	[Fact]
	public async Task EveryRowOfTheRepeatedAutumnHourComesOutWithItsStoredTime()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		foreach (var timestamp in _autumnTimestamps)
		{
			await AppendAsync(Database, SteadyPenId, timestamp, 1, cancellationToken: cancellationToken);
		}

		var slice = await ReadSliceAsync([SteadyPenId], _autumnFrom, _autumnTo);

		slice.Select(row => row.Time).Should().Equal(_autumnTimestamps);
	}

	[Fact]
	public async Task TheCountEqualsTheSeedsPlusTheRowsOfTheSlicesTilingTheRange()
	{
		var penIds = new[] { SteadyPenId, SeedOnlyPenId, StalePenId, BoundaryPenId };
		var read = (await ReadAsTheExporterDoesAsync(penIds)).Count;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var counted = await services.GetRequiredService<IArchiveRowReader>()
			.CountRowsAsync(penIds, _from, _to, TestContext.Current.CancellationToken);

		counted.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(counted));
		counted.Value.Should().Be(read);
	}

	[Fact]
	public async Task ADroppedTrendsTableFailsEveryReadNamingTrends()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await ArchiveDatabase.ExecuteAsync(
			Database.WriterConnectionString,
			ArchiveReadSupport.DropTrendsCommand,
			cancellationToken);

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var reader = services.GetRequiredService<IArchiveRowReader>();

		var seeds = await reader.ReadSeedRowsAsync([SteadyPenId], _from, cancellationToken);
		var slice = await reader.ReadSliceAsync([SteadyPenId], _from, _to, cancellationToken);
		var counted = await reader.CountRowsAsync([SteadyPenId], _from, _to, cancellationToken);

		AssertTableMissing(seeds);
		AssertTableMissing(slice);
		AssertTableMissing(counted);
	}

	[Fact]
	public async Task ACancelledReadThrowsInsteadOfFailing()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var reader = services.GetRequiredService<IArchiveRowReader>();
		var cancelled = new CancellationToken(true);

		await reader.Invoking(r => r.ReadSeedRowsAsync([SteadyPenId], _from, cancelled))
			.Should().ThrowAsync<OperationCanceledException>();
		await reader.Invoking(r => r.ReadSliceAsync([SteadyPenId], _from, _to, cancelled))
			.Should().ThrowAsync<OperationCanceledException>();
		await reader.Invoking(r => r.CountRowsAsync([SteadyPenId], _from, _to, cancelled))
			.Should().ThrowAsync<OperationCanceledException>();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ACancelDuringARunningStatement_ThrowsOnceTheLockedTableHoldsItBack(bool readsSeeds)
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var reader = services.GetRequiredService<IArchiveRowReader>();

		await using var locker = new NpgsqlConnection(Database.WriterConnectionString);

		await locker.OpenAsync(cancellationToken);
		await using var transaction = await locker.BeginTransactionAsync(cancellationToken);
		await using (var lockCommand = new NpgsqlCommand(
			"LOCK TABLE public.trends IN ACCESS EXCLUSIVE MODE", locker, transaction))
		{
			await lockCommand.ExecuteNonQueryAsync(cancellationToken);
		}

		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var read = readsSeeds
			? reader.ReadSeedRowsAsync([SteadyPenId], _from, cancellation.Token)
			: reader.ReadSliceAsync([SteadyPenId], _from, _to, cancellation.Token);

		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		read.IsCompleted.Should().BeFalse("the table lock holds the statement back");

		await cancellation.CancelAsync();

		await ((Func<Task>)(() => read)).Should().ThrowAsync<OperationCanceledException>();
	}

	private static IReadOnlyList<ArchiveRow> SeededRows()
	{
		return
		[
			// Steady: the older seed candidate loses to the newer one; the window holds the three markers; the
			// row at the range end and the coarse row are not part of the answer.
			Raw(SteadyPenId, Day.AddMinutes(30), ArchiveRow.OrdinaryQuality),
			Raw(SteadyPenId, Day.AddMinutes(50), ArchiveRow.OrdinaryQuality),
			Raw(SteadyPenId, Day.AddMinutes(70), ArchiveRow.FirstAfterBreakQuality),
			Raw(SteadyPenId, Day.AddMinutes(80), ArchiveRow.LastBeforeBreakQuality),
			new ArchiveRow(SteadyPenId, LayerThinner.MinuteLayer, Day.AddMinutes(75), SteadyPenId, ArchiveRow.OrdinaryQuality),
			Raw(SteadyPenId, Day.AddMinutes(90), ArchiveRow.OrdinaryQuality),
			Raw(SteadyPenId, _to, ArchiveRow.OrdinaryQuality),
			Raw(SeedOnlyPenId, Day.AddMinutes(40), ArchiveRow.OrdinaryQuality),
			Raw(StalePenId, _from.AddDays(-1).AddSeconds(-1), ArchiveRow.OrdinaryQuality),
			Raw(BoundaryPenId, _from.AddDays(-1), ArchiveRow.OrdinaryQuality)
		];
	}

	private static ArchiveRow Raw(int penId, DateTime timestamp, int quality)
	{
		return new ArchiveRow(penId, ArchiveRow.RawLayer, timestamp, penId, quality);
	}

	private static void AssertTableMissing<T>(Result<T> result)
	{
		result.IsFailed.Should().BeTrue();

		var error = result.Errors.OfType<ArchiveError>().Should().ContainSingle().Which;

		error.Kind.Should().Be(ArchiveFault.TableMissing);
		error.Detail.Should().Be("trends");
	}

	private async Task<IReadOnlyList<StoredArchiveRow>> ReadAsTheExporterDoesAsync(IReadOnlyList<int> penIds)
	{
		var rows = new List<StoredArchiveRow>(await ReadSeedsAsync(penIds));

		foreach (var (sliceFrom, sliceTo) in ArchiveRowSlices.Tile(_from, _to))
		{
			rows.AddRange(await ReadSliceAsync(penIds, sliceFrom, sliceTo));
		}

		return rows;
	}

	private async Task<IReadOnlyList<StoredArchiveRow>> ReadSeedsAsync(IReadOnlyList<int> penIds)
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var result = await services.GetRequiredService<IArchiveRowReader>()
			.ReadSeedRowsAsync(penIds, _from, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		return result.Value;
	}

	private async Task<IReadOnlyList<StoredArchiveRow>> ReadSliceAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to)
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var result = await services.GetRequiredService<IArchiveRowReader>()
			.ReadSliceAsync(penIds, from, to, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));

		return result.Value;
	}
}
