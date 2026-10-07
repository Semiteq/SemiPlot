using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenScaleModelTests
{
	private static readonly DateTime _origin = new(2026, 6, 16, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Compute_ActivePenAxis_SurfacesItsRangeAndIsActive()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1),
			new PenScaleSettings(PenId: 2)
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = Envelope(1, (0.0, 10.0), (2.0, 12.0)),
			[2] = Envelope(2, (100.0, 200.0))
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var active = scales.Single(scale => scale.PenId == 1);
		active.IsActive.Should().BeTrue();
		active.Min.Should().BeApproximately(0.0 - (12.0 * 0.05), 1e-9);
		active.Max.Should().BeApproximately(12.0 + (12.0 * 0.05), 1e-9);

		scales.Single(scale => scale.PenId == 2).IsActive.Should().BeFalse();
	}

	[Fact]
	public void Compute_NonActivePens_AutoscaleIndividuallyOnSeparateAxes()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1),
			new PenScaleSettings(PenId: 2)
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = Envelope(1, (0.0, 5.0)),
			[2] = Envelope(2, (-3.0, 7.0))
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		scales.Should().HaveCount(2);
		var second = scales.Single(scale => scale.PenId == 2);
		second.IsActive.Should().BeFalse();
		second.Min.Should().BeApproximately(-3.0 - (10.0 * 0.05), 1e-9);
		second.Max.Should().BeApproximately(7.0 + (10.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_AutoMode_FitsOnlyValuesInsideVisibleWindow()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1) };

		var timestamps = new[] { _origin, _origin.AddHours(1), _origin.AddHours(2), _origin.AddHours(3) };
		var min = new[] { 0.0, 50.0, 1000.0, -500.0 };
		var max = new[] { 1.0, 60.0, 2000.0, -400.0 };
		var center = new[] { 0.5, 55.0, 1500.0, -450.0 };
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = new PenHistoryEnvelope(1, timestamps, min, max, center)
		};

		var scales = model.Compute(
			settings, envelopes, activePenId: 1, _origin.AddMinutes(30), _origin.AddHours(1).AddMinutes(30));

		var windowed = scales.Should().ContainSingle().Which;
		windowed.Min.Should().BeApproximately(50.0 - (10.0 * 0.05), 1e-9);
		windowed.Max.Should().BeApproximately(60.0 + (10.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_AutoModeWithNoEnvelopeAtAll_FallsBackToTheDefaultRange()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1) };

		var scales = model.Compute(
			settings, new Dictionary<int, PenHistoryEnvelope>(), activePenId: 1, _origin, _origin.AddHours(1));

		var empty = scales.Should().ContainSingle().Which;
		empty.Min.Should().Be(0.0);
		empty.Max.Should().Be(1.0);
	}

	[Fact]
	public void Compute_AutoModeOverAStickyWindowPastTheLastColumn_KeepsThePenScale()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = Envelope(1, (900.0, 1000.0), (950.0, 1100.0))
		};

		// The live edge has moved a day past the newest fetched column, which the sticky advance never
		// requeries.
		var scales = model.Compute(
			settings, envelopes, activePenId: 1, _origin.AddDays(1), _origin.AddDays(1).AddHours(1));

		var live = scales.Should().ContainSingle().Which;
		live.Min.Should().BeApproximately(900.0 - (200.0 * 0.05), 1e-9);
		live.Max.Should().BeApproximately(1100.0 + (200.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_ManualMode_UsesFixedLimitsWithoutConsultingData()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, Mode: ScaleMode.Manual, ManualMin: -2.0, ManualMax: 8.0)
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (1000.0, 2000.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var manual = scales.Should().ContainSingle().Which;
		manual.Min.Should().Be(-2.0);
		manual.Max.Should().Be(8.0);
		manual.Mode.Should().Be(ScaleMode.Manual);
	}

	[Fact]
	public void Compute_LogarithmicAxis_DropsNonPositiveValuesBeforeComputingRange()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, IsLogarithmic: true) };

		var timestamps = new[] { _origin, _origin.AddHours(1), _origin.AddHours(2) };
		var min = new[] { -5.0, 2.0, 0.0 };
		var max = new[] { 0.0, 4.0, 50.0 };
		var center = new[] { -2.0, 3.0, 25.0 };
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = new PenHistoryEnvelope(1, timestamps, min, max, center)
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(3));

		var logScale = scales.Should().ContainSingle().Which;
		var paddingDecades = (Math.Log10(50.0) - Math.Log10(2.0)) * 0.05;
		logScale.IsLogarithmic.Should().BeTrue();
		logScale.Min.Should().BeApproximately(2.0 / Math.Pow(10.0, paddingDecades), 1e-9);
		logScale.Max.Should().BeApproximately(50.0 * Math.Pow(10.0, paddingDecades), 1e-9);
	}

	[Fact]
	public void Compute_LogarithmicAxisOverDecades_PadsBeyondBothEnds()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, IsLogarithmic: true) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (1e-6, 1e-4), (1e-3, 1e-2)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(2));

		var logScale = scales.Should().ContainSingle().Which;
		logScale.Min.Should().BeGreaterThan(0.0).And.BeLessThan(1e-6);
		logScale.Max.Should().BeGreaterThan(1e-2);
	}

	[Fact]
	public void Compute_FlatLogarithmicLine_PadsHalfADecadeOnEachSide()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, IsLogarithmic: true) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (0.3, 0.3)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var flat = scales.Should().ContainSingle().Which;
		flat.Min.Should().BeLessThan(0.3);
		flat.Max.Should().BeGreaterThan(0.3);
		flat.Min.Should().BeApproximately(0.3 / Math.Sqrt(10.0), 1e-12);
		flat.Max.Should().BeApproximately(0.3 * Math.Sqrt(10.0), 1e-12);
	}

	[Theory]
	[InlineData(1.0, double.PositiveInfinity)]
	[InlineData(double.Epsilon, 1.0)]
	[InlineData(double.Epsilon, double.MaxValue)]
	[InlineData(double.MaxValue, double.PositiveInfinity)]
	public void Compute_LogarithmicAxisAtTheEdgesOfTheDoubleRange_KeepsFiniteDecadeLimits(double low, double high)
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, IsLogarithmic: true) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (low, low), (high, high)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(2));

		var logScale = scales.Should().ContainSingle().Which;
		double.IsFinite(Math.Log10(logScale.Min)).Should().BeTrue();
		double.IsFinite(Math.Log10(logScale.Max)).Should().BeTrue();
		logScale.Min.Should().BeLessThan(logScale.Max);
	}

	[Fact]
	public void Compute_LogarithmicAxisWithNoPositiveValues_FallsBackToPositiveDefaultRange()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, IsLogarithmic: true) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (-10.0, -1.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var logScale = scales.Should().ContainSingle().Which;
		logScale.Min.Should().BeGreaterThan(0.0);
		logScale.Max.Should().BeGreaterThan(logScale.Min);
	}

	[Fact]
	public void Compute_AutoModeIgnoresNaNGapColumns()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1) };

		var timestamps = new[] { _origin, _origin.AddHours(1), _origin.AddHours(2) };
		var min = new[] { 4.0, double.NaN, 6.0 };
		var max = new[] { 8.0, double.NaN, 10.0 };
		var center = new[] { 6.0, double.NaN, 8.0 };
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = new PenHistoryEnvelope(1, timestamps, min, max, center)
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(2));

		var scale = scales.Should().ContainSingle().Which;
		double.IsNaN(scale.Min).Should().BeFalse();
		scale.Min.Should().BeApproximately(4.0 - (6.0 * 0.05), 1e-9);
		scale.Max.Should().BeApproximately(10.0 + (6.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_FlatLine_PadsByHalfAUnitOnEachSide()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (5.0, 5.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var scale = scales.Should().ContainSingle().Which;
		scale.Min.Should().BeApproximately(4.5, 1e-9);
		scale.Max.Should().BeApproximately(5.5, 1e-9);
	}

	[Fact]
	public void Compute_ManualMode_SwapsInvertedLimits()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, Mode: ScaleMode.Manual, ManualMin: 90.0, ManualMax: 10.0)
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (0.0, 1.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var manual = scales.Should().ContainSingle().Which;
		manual.Min.Should().Be(10.0);
		manual.Max.Should().Be(90.0);
	}

	[Fact]
	public void Compute_ManualLogarithmic_SanitizesNonPositiveLowerBound()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(
				PenId: 1, Mode: ScaleMode.Manual, ManualMin: -5.0, ManualMax: 100.0, IsLogarithmic: true)
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (1.0, 50.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var manual = scales.Should().ContainSingle().Which;
		manual.Min.Should().BeGreaterThan(0.0);
		manual.Max.Should().Be(100.0);
	}

	[Theory]
	[InlineData(1.0, double.PositiveInfinity, true, 1.0, 10.0)]
	[InlineData(1.0, double.NaN, true, 1.0, 10.0)]
	[InlineData(double.NegativeInfinity, 100.0, true, 1.0, 10.0)]
	[InlineData(1.0, double.PositiveInfinity, false, 0.0, 1.0)]
	[InlineData(double.NaN, 100.0, false, 0.0, 1.0)]
	public void Compute_ManualModeWithANonFiniteBound_FallsBackToTheDefaultRange(
		double manualMin,
		double manualMax,
		bool isLogarithmic,
		double defaultMin,
		double defaultMax)
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, Mode: ScaleMode.Manual, ManualMin: manualMin, ManualMax: manualMax)
			{
				IsLogarithmic = isLogarithmic
			}
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (2.0, 50.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var manual = scales.Should().ContainSingle().Which;
		manual.Min.Should().Be(defaultMin);
		manual.Max.Should().Be(defaultMax);
	}

	private static PenHistoryEnvelope Envelope(int penId, params (double Min, double Max)[] columns)
	{
		var timestamps = new DateTime[columns.Length];
		var min = new double[columns.Length];
		var max = new double[columns.Length];
		var center = new double[columns.Length];

		for (var index = 0; index < columns.Length; index++)
		{
			timestamps[index] = _origin.AddHours(index);
			min[index] = columns[index].Min;
			max[index] = columns[index].Max;
			center[index] = (columns[index].Min + columns[index].Max) / 2.0;
		}

		return new PenHistoryEnvelope(penId, timestamps, min, max, center);
	}
}
