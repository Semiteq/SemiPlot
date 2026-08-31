using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// A caller-argument fault answers before the connection opens, so the provider these tests resolve over
// an address nothing answers returns the failed Result with no network round trip — an attempted connect
// would surface as ArchiveFault.Unreachable instead of the message each test asserts.
//
// This class asserts message text where the rest of the suite asserts error types and structured fields.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class HistoryArgumentGuardTests
{
	private const int TargetColumnCount = 100;

	private static readonly DateTime _fromUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

	private static async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryAsync(
		IReadOnlyList<long> penIds,
		DateTime toUtc,
		int targetColumnCount,
		AggregationLayer layer = AggregationLayer.Raw)
	{
		using var services = new ServiceCollection()
			.AddLogging()
			.AddPostgresData(ConnectionSettingsFactory.Create())
			.BuildServiceProvider();

		return await services.GetRequiredService<IDataProvider>()
			.QueryHistoryAsync(penIds, _fromUtc, toUtc, layer, targetColumnCount);
	}

	private static string SingleMessage<T>(Result<T> result)
	{
		Assert.True(result.IsFailed);

		return Assert.Single(result.Errors).Message;
	}

	[Fact]
	public async Task AWindowEndingBeforeItStartsFails()
	{
		var toUtc = _fromUtc.AddMinutes(-1);

		var result = await QueryAsync([7L], toUtc, TargetColumnCount);

		Assert.Equal(
			$"Invalid range: fromUtc ({_fromUtc:O}) is after toUtc ({toUtc:O}).",
			SingleMessage(result));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task ATargetColumnCountBelowOneFails(int targetColumnCount)
	{
		var result = await QueryAsync([7L], _fromUtc.AddMinutes(1), targetColumnCount);

		Assert.Equal(
			$"Invalid target column count: {targetColumnCount} (must be at least one).",
			SingleMessage(result));
	}

	// Both ends are covered: a silent wrap would map either onto a different pen.
	[Theory]
	[InlineData((long)int.MaxValue + 1)]
	[InlineData((long)int.MinValue - 1)]
	public async Task APenIdentifierOutsideTheArchivesIntegerRangeFails(long penId)
	{
		var result = await QueryAsync([7L, penId], _fromUtc.AddMinutes(1), TargetColumnCount);

		Assert.Equal(
			$"Invalid pen identifier: {penId} (must fit the archive's 32-bit identifier column).",
			SingleMessage(result));
	}

	[Fact]
	public async Task ANullPenListThrows()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => QueryAsync(null!, _fromUtc.AddMinutes(1), TargetColumnCount));
	}

	// A value outside the closed enum is the same class of caller defect as a null list, so it leaves as an
	// exception rather than through the Result channel.
	[Fact]
	public async Task AnUndefinedAggregationLayerThrows()
	{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
			() => QueryAsync([7L], _fromUtc.AddMinutes(1), TargetColumnCount, (AggregationLayer)99));
	}

	// Two caller defects at once. The provider orders its guards so the range and target checks answer
	// first and the failed Result wins over the layer throw, which is what a caller reading the Result
	// channel depends on.
	[Fact]
	public async Task AnInvertedWindowAnswersAheadOfAnUndefinedAggregationLayer()
	{
		var toUtc = _fromUtc.AddMinutes(-1);

		var result = await QueryAsync([7L], toUtc, TargetColumnCount, (AggregationLayer)99);

		Assert.Equal(
			$"Invalid range: fromUtc ({_fromUtc:O}) is after toUtc ({toUtc:O}).",
			SingleMessage(result));
	}

	[Fact]
	public async Task ATargetColumnCountBelowOneAnswersAheadOfAnUndefinedAggregationLayer()
	{
		var result = await QueryAsync([7L], _fromUtc.AddMinutes(1), 0, (AggregationLayer)99);

		Assert.Equal(
			"Invalid target column count: 0 (must be at least one).",
			SingleMessage(result));
	}
}
