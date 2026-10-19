using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartRealtimeApplierTests
{
	private const double SampledValue = 7.0;
	private static readonly DateTime _start = ChartTestBuilder.From;

	[Fact]
	public void AtRaw_AValueAndThenABreak_AppendARealColumnAndThenANaNColumn()
	{
		var state = NewState();

		Apply(state, foldIntoColumn: false, (_start, SampledValue), (_start.AddMilliseconds(1), null));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		columns[0].Center.Should().Be(SampledValue);
		double.IsNaN(columns[1].Center).Should().BeTrue();
		columns[1].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddMilliseconds(1)));
		state.CurrentValue.Should().Be(SampledValue);
	}

	[Fact]
	public void AtACoarseLayer_TheValueFoldsAndTheBreakAppendsPastTheFoldedColumn()
	{
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);

		Apply(
			state,
			foldIntoColumn: true,
			(_start.AddSeconds(1), SampledValue),
			(_start.AddSeconds(1).AddMilliseconds(1), null));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		columns[0].Min.Should().Be(1.0);
		columns[0].Max.Should().Be(SampledValue);
		columns[0].Center.Should().Be(SampledValue);
		double.IsNaN(columns[1].Center).Should().BeTrue();
		columns[1].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddSeconds(1).AddMilliseconds(1)));
		state.CurrentValue.Should().Be(SampledValue);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	[Fact]
	public void AtACoarseLayer_OnAPenWithNoColumn_TheBreakAppendsAndTheNextValueOpensAColumnPastIt()
	{
		const double NextValue = 9.0;
		var state = NewState();

		Apply(
			state,
			foldIntoColumn: true,
			(_start, SampledValue),
			(_start.AddMilliseconds(1), null),
			(_start.AddSeconds(1), NextValue));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(2);
		double.IsNaN(columns[0].Center).Should().BeTrue();
		columns[1].Center.Should().Be(NextValue);
		columns[1].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddSeconds(1)));
		state.CurrentValue.Should().Be(NextValue);
	}

	private static void Apply(TrendPenState state, bool foldIntoColumn, params (DateTime Utc, double? Value)[] samples)
	{
		var timestamps = samples.Select(sample => sample.Utc).ToArray();
		var batch = new RealtimeBatch(
			timestamps,
			[new PenRealtimeValues(state.Pen.PenId, timestamps, [.. samples.Select(sample => sample.Value)])]);
		var applier = new ChartRealtimeApplier(
			new Dictionary<int, TrendPenState> { [state.Pen.PenId] = state },
			new ChartNavigationController());

		applier.Apply(batch, foldIntoColumn);
	}

	private static TrendPenState NewState()
	{
		return new TrendPenState(FakeDataProvider.FirstPen, new EnvelopeLine());
	}
}
