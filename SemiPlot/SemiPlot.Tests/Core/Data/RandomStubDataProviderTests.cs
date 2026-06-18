using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Stub;

using Xunit;

namespace SemiPlot.Tests.Core.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class RandomStubDataProviderTests
{
	private const int TargetColumns = 256;

	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Pens_ExposesCatalog()
	{
		var provider = CreateProvider(new TestScheduler());

		provider.Pens.Should().NotBeEmpty();
		provider.Pens.Select(pen => pen.PenId).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public async Task QueryHistoryAsync_SameInputs_ProducesByteForByteIdenticalEnvelopes()
	{
		var first = await CreateProvider(new TestScheduler())
			.QueryHistoryAsync(PenIds(), _from, _to, AggregationLayer.Minute, TargetColumns);
		var second = await CreateProvider(new TestScheduler())
			.QueryHistoryAsync(PenIds(), _from, _to, AggregationLayer.Minute, TargetColumns);

		first.IsSuccess.Should().BeTrue();
		second.IsSuccess.Should().BeTrue();

		var firstCenters = first.Value.SelectMany(envelope => envelope.Center).ToArray();
		var secondCenters = second.Value.SelectMany(envelope => envelope.Center).ToArray();
		firstCenters.Should().Equal(secondCenters);

		var firstStamps = first.Value.SelectMany(envelope => envelope.Timestamps).ToArray();
		var secondStamps = second.Value.SelectMany(envelope => envelope.Timestamps).ToArray();
		firstStamps.Should().Equal(secondStamps);
	}

	[Theory]
	[InlineData(AggregationLayer.Raw)]
	[InlineData(AggregationLayer.Minute)]
	[InlineData(AggregationLayer.Hour)]
	[InlineData(AggregationLayer.Day)]
	public async Task QueryHistoryAsync_EnvelopeIsDecimatedAndMonotonic(AggregationLayer layer)
	{
		var provider = CreateProvider(new TestScheduler());

		var result = await provider.QueryHistoryAsync(PenIds(), _from, _to, layer, TargetColumns);

		result.IsSuccess.Should().BeTrue();
		var envelope = result.Value.Should().ContainSingle().Subject;

		envelope.Timestamps.Should().NotBeEmpty();
		envelope.Timestamps.Should().BeInAscendingOrder();
		envelope.Timestamps[0].Should().BeOnOrAfter(_from);
		envelope.Timestamps[^1].Should().BeOnOrBefore(_to);
		envelope.Min.Should().HaveCount(envelope.Timestamps.Count);
		envelope.Max.Should().HaveCount(envelope.Timestamps.Count);
		envelope.Center.Should().HaveCount(envelope.Timestamps.Count);
	}

	[Fact]
	public async Task QueryHistoryAsync_ColumnCountStaysBoundedByTarget()
	{
		var provider = CreateProvider(new TestScheduler());

		var result = await provider.QueryHistoryAsync(PenIds(), _from, _to, AggregationLayer.Raw, TargetColumns);

		var envelope = result.Value.Single();
		envelope.Timestamps.Count.Should().BeLessThanOrEqualTo(TargetColumns * 4);
	}

	[Fact]
	public async Task QueryHistoryAsync_FiniteBandValuesStayWithinPenRange()
	{
		var provider = CreateProvider(new TestScheduler());
		var penId = provider.Pens[0].PenId;
		var synthetic = SyntheticPenCatalog.Build().Single(candidate => candidate.PenId == penId);

		var result = await provider.QueryHistoryAsync([penId], _from, _to, AggregationLayer.Minute, TargetColumns);

		var envelope = result.Value.Single();
		var finiteValues = envelope.Min
			.Concat(envelope.Max)
			.Concat(envelope.Center)
			.Where(double.IsFinite)
			.ToArray();
		finiteValues.Should().NotBeEmpty();
		finiteValues.Should().OnlyContain(value => value >= synthetic.MinValue && value <= synthetic.MaxValue);
	}

	[Fact]
	public async Task QueryHistoryAsync_BadQualityProducesNaNGapColumns()
	{
		var provider = CreateProvider(new TestScheduler());

		// A full hour at one sample/second guarantees the deterministic bad-quality cadence fires.
		var result = await provider.QueryHistoryAsync(PenIds(), _from, _to, AggregationLayer.Raw, TargetColumns);

		var envelope = result.Value.Single();
		envelope.Center.Should().Contain(value => double.IsNaN(value));
		envelope.Min.Should().Contain(value => double.IsNaN(value));
		envelope.Max.Should().Contain(value => double.IsNaN(value));
	}

	[Fact]
	public async Task QueryHistoryAsync_FromAfterTo_Fails()
	{
		var provider = CreateProvider(new TestScheduler());

		var result = await provider.QueryHistoryAsync(PenIds(), _to, _from, AggregationLayer.Minute, TargetColumns);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle()
			.Which.Message.Should().Contain("Invalid range");
	}

	[Fact]
	public async Task QueryHistoryAsync_NonPositiveTarget_Fails()
	{
		var provider = CreateProvider(new TestScheduler());

		var result = await provider.QueryHistoryAsync(PenIds(), _from, _to, AggregationLayer.Minute, 0);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle()
			.Which.Message.Should().Contain("target column count");
	}

	[Fact]
	public async Task QueryHistoryAsync_EmptyPenList_ReturnsNoEnvelopes()
	{
		var provider = CreateProvider(new TestScheduler());

		var result = await provider.QueryHistoryAsync([], _from, _to, AggregationLayer.Minute, TargetColumns);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEmpty();
	}

	[Fact]
	public async Task QueryHistoryAsync_UnknownPenIds_AreIgnored()
	{
		var provider = CreateProvider(new TestScheduler());

		var result = await provider.QueryHistoryAsync([-1, -2], _from, _to, AggregationLayer.Minute, TargetColumns);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().BeEmpty();
	}

	[Fact]
	public void Subscribe_EmitsOnlyForSubscribedPenIds()
	{
		var scheduler = new TestScheduler();
		var provider = CreateProvider(scheduler);
		var subscribedPenId = provider.Pens[0].PenId;
		var otherPenId = provider.Pens[1].PenId;

		var observed = new List<IReadOnlyList<Sample>>();
		using var subscription = provider.Subscribe([subscribedPenId]).Subscribe(observed.Add);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(3).Ticks);

		observed.Should().NotBeEmpty();
		var emittedPenIds = observed.SelectMany(batch => batch).Select(sample => sample.PenId).Distinct();
		emittedPenIds.Should().ContainSingle().Which.Should().Be(subscribedPenId);
		emittedPenIds.Should().NotContain(otherPenId);
	}

	[Fact]
	public void Subscribe_ValuesStayFiniteAndInRange()
	{
		var scheduler = new TestScheduler();
		var provider = CreateProvider(scheduler);
		var pen = provider.Pens[0];

		var observed = new List<IReadOnlyList<Sample>>();
		using var subscription = provider.Subscribe([pen.PenId]).Subscribe(observed.Add);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(10).Ticks);

		var synthetic = SyntheticPenCatalog.Build().Single(candidate => candidate.PenId == pen.PenId);
		var values = observed.SelectMany(batch => batch).Select(sample => sample.Value).ToArray();
		values.Should().NotBeEmpty();
		values.Should().OnlyContain(value => double.IsFinite(value));
		values.Should().OnlyContain(value => value >= synthetic.MinValue && value <= synthetic.MaxValue);
	}

	[Fact]
	public void Subscribe_TimestampsAdvanceFromWallClockAnchor()
	{
		var scheduler = new TestScheduler();
		var anchor = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
		var interval = TimeSpan.FromSeconds(1);
		var provider = new RandomStubDataProvider(scheduler, realtimeInterval: interval, utcNow: () => anchor);
		var penId = provider.Pens[0].PenId;

		var observed = new List<IReadOnlyList<Sample>>();
		using var subscription = provider.Subscribe([penId]).Subscribe(observed.Add);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(3).Ticks);

		var stamps = observed.SelectMany(batch => batch).Select(sample => sample.TimestampUtc).ToArray();
		stamps.Should().Equal(
			anchor + interval,
			anchor + interval + interval,
			anchor + interval + interval + interval);
	}

	[Fact]
	public void Subscribe_EmptyPenList_EmitsEmptyBatches()
	{
		var scheduler = new TestScheduler();
		var provider = CreateProvider(scheduler);

		var observed = new List<IReadOnlyList<Sample>>();
		using var subscription = provider.Subscribe([]).Subscribe(observed.Add);

		scheduler.AdvanceBy(TimeSpan.FromSeconds(2).Ticks);

		observed.Should().NotBeEmpty();
		observed.Should().OnlyContain(batch => batch.Count == 0);
	}

	[Fact]
	public async Task QueryArchiveExtentAsync_ReturnsSevenDayDepthEndingAtNow()
	{
		var now = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
		var provider = new RandomStubDataProvider(new TestScheduler(), utcNow: () => now);

		var result = await provider.QueryArchiveExtentAsync();

		result.IsSuccess.Should().BeTrue();
		result.Value.LastUtc.Should().Be(now);
		result.Value.FirstUtc.Should().Be(now - TimeSpan.FromDays(7.0));
	}

	private static RandomStubDataProvider CreateProvider(IScheduler scheduler)
	{
		return new(scheduler);
	}

	private static IReadOnlyList<long> PenIds()
	{
		return [new RandomStubDataProvider(new TestScheduler()).Pens[0].PenId];
	}
}
