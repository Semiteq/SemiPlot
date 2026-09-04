using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// A caller-argument fault answers before the connection opens, so an address nothing answers still
// returns the failed Result with no network round trip. This class asserts message text where the
// rest of the suite asserts error types and structured fields.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class HistoryArgumentGuardTests
{
	private const int TargetColumnCount = 100;

	private static readonly DateTime _fromUtc = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

	private static async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryAsync(
		IReadOnlyList<int> penIds,
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
		result.IsFailed.Should().BeTrue();

		return result.Errors.Should().ContainSingle().Which.Message;
	}

	[Fact]
	public async Task AWindowEndingBeforeItStartsFails()
	{
		var toUtc = _fromUtc.AddMinutes(-1);

		var result = await QueryAsync([7], toUtc, TargetColumnCount);

		SingleMessage(result).Should().Be($"Invalid range: fromUtc ({_fromUtc:O}) is after toUtc ({toUtc:O}).");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public async Task ATargetColumnCountBelowOneFails(int targetColumnCount)
	{
		var result = await QueryAsync([7], _fromUtc.AddMinutes(1), targetColumnCount);

		SingleMessage(result).Should().Be(
			$"Invalid target column count: {targetColumnCount} (must be at least one).");
	}

	[Fact]
	public async Task ANullPenListThrows()
	{
		Func<Task> act = () => QueryAsync(null!, _fromUtc.AddMinutes(1), TargetColumnCount);

		await act.Should().ThrowAsync<ArgumentNullException>();
	}

	// A value outside the closed enum is the same class of caller defect as a null list, so it leaves as an
	// exception rather than through the Result channel.
	[Fact]
	public async Task AnUndefinedAggregationLayerThrows()
	{
		Func<Task> act = () => QueryAsync([7], _fromUtc.AddMinutes(1), TargetColumnCount, (AggregationLayer)99);

		await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
	}

	// Two caller defects at once. The provider orders its guards so the range and target checks answer
	// first and the failed Result wins over the layer throw, which is what a caller reading the Result
	// channel depends on.
	[Fact]
	public async Task AnInvertedWindowAnswersAheadOfAnUndefinedAggregationLayer()
	{
		var toUtc = _fromUtc.AddMinutes(-1);

		var result = await QueryAsync([7], toUtc, TargetColumnCount, (AggregationLayer)99);

		SingleMessage(result).Should().Be($"Invalid range: fromUtc ({_fromUtc:O}) is after toUtc ({toUtc:O}).");
	}

	[Fact]
	public async Task ATargetColumnCountBelowOneAnswersAheadOfAnUndefinedAggregationLayer()
	{
		var result = await QueryAsync([7], _fromUtc.AddMinutes(1), 0, (AggregationLayer)99);

		SingleMessage(result).Should().Be("Invalid target column count: 0 (must be at least one).");
	}
}
