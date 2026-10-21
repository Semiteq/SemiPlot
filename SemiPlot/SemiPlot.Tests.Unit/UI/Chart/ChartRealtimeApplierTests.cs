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
		columns.Should().HaveCount(3, "the first value opens behind its own break");
		double.IsNaN(columns[0].Center).Should().BeTrue();
		columns[1].Center.Should().Be(SampledValue);
		double.IsNaN(columns[2].Center).Should().BeTrue();
		columns[2].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddMilliseconds(1)));
		state.CurrentValue.Should().Be(SampledValue);
	}

	[Fact]
	public void AtACoarseLayer_TheValueFoldsAndTheBreakAppendsPastTheFoldedColumn()
	{
		const double FirstValue = 3.0;
		var state = NewState();
		state.LoadHistory(new PenHistoryEnvelope(1, [_start], [1.0], [5.0], [2.0]), _start);

		Apply(
			state,
			foldIntoColumn: true,
			(_start.AddSeconds(1), FirstValue),
			(_start.AddSeconds(2), SampledValue),
			(_start.AddSeconds(2).AddMilliseconds(1), null));

		var columns = state.Line.SnapshotColumns();
		columns.Should().HaveCount(4, "the first value opens a column behind its own break");
		double.IsNaN(columns[1].Center).Should().BeTrue();
		columns[2].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddSeconds(1)));
		columns[2].Min.Should().Be(FirstValue);
		columns[2].Max.Should().Be(SampledValue);
		columns[2].Center.Should().Be(SampledValue);
		double.IsNaN(columns[3].Center).Should().BeTrue();
		columns[3].X.Should().Be(LocalTimeAxis.ToAxis(_start.AddSeconds(2).AddMilliseconds(1)));
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
