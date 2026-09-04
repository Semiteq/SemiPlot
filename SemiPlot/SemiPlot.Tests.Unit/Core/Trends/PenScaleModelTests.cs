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
	public void Compute_ActivePenAxis_SurfacesItsRangeAndIsActiveAndVisible()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, AxisKey: "pressure"),
			new PenScaleSettings(PenId: 2, AxisKey: "temperature")
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = Envelope(1, (0.0, 10.0), (2.0, 12.0)),
			[2] = Envelope(2, (100.0, 200.0))
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var active = scales.Single(scale => scale.AxisKey == "pressure");
		active.IsActive.Should().BeTrue();
		active.IsVisible.Should().BeTrue();
		active.Min.Should().BeApproximately(0.0 - (12.0 * 0.05), 1e-9);
		active.Max.Should().BeApproximately(12.0 + (12.0 * 0.05), 1e-9);

		scales.Single(scale => scale.AxisKey == "temperature").IsActive.Should().BeFalse();
	}

	[Fact]
	public void Compute_NonActivePens_AutoscaleIndividuallyOnSeparateAxes()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, AxisKey: "a"),
			new PenScaleSettings(PenId: 2, AxisKey: "b")
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = Envelope(1, (0.0, 5.0)),
			[2] = Envelope(2, (-3.0, 7.0))
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		scales.Should().HaveCount(2);
		var axisB = scales.Single(scale => scale.AxisKey == "b");
		axisB.IsActive.Should().BeFalse();
		axisB.PenIds.Should().ContainSingle().Which.Should().Be(2);
		axisB.Min.Should().BeApproximately(-3.0 - (10.0 * 0.05), 1e-9);
		axisB.Max.Should().BeApproximately(7.0 + (10.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_SharedGroup_ProducesOneScaleSpanningAllMembers()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, AxisKey: "heaters"),
			new PenScaleSettings(PenId: 2, AxisKey: "heaters"),
			new PenScaleSettings(PenId: 3, AxisKey: "heaters")
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope>
		{
			[1] = Envelope(1, (10.0, 20.0)),
			[2] = Envelope(2, (5.0, 30.0)),
			[3] = Envelope(3, (15.0, 25.0))
		};

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var shared = scales.Should().ContainSingle().Which;
		shared.AxisKey.Should().Be("heaters");
		shared.PenIds.Should().BeEquivalentTo(new[] { 1, 2, 3 });
		shared.Min.Should().BeApproximately(5.0 - (25.0 * 0.05), 1e-9);
		shared.Max.Should().BeApproximately(30.0 + (25.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_AutoscaleToWindow_FitsOnlyValuesInsideVisibleWindow()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, AxisKey: "a", Mode: ScaleMode.AutoscaleToWindow) };

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
	public void Compute_ManualMode_UsesFixedLimitsWithoutConsultingData()
	{
		var model = new PenScaleModel();
		var settings = new[]
		{
			new PenScaleSettings(PenId: 1, AxisKey: "a", Mode: ScaleMode.Manual, ManualMin: -2.0, ManualMax: 8.0)
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
		var settings = new[] { new PenScaleSettings(PenId: 1, AxisKey: "a", IsLogarithmic: true) };

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
		logScale.IsLogarithmic.Should().BeTrue();
		logScale.Min.Should().BeGreaterThan(0.0, "a log axis lower bound is clamped positive after padding");
		logScale.Max.Should().BeApproximately(50.0 + (48.0 * 0.05), 1e-9);
	}

	[Fact]
	public void Compute_LogarithmicAxisWithNoPositiveValues_FallsBackToPositiveDefaultRange()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, AxisKey: "a", IsLogarithmic: true) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (-10.0, -1.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var logScale = scales.Should().ContainSingle().Which;
		logScale.Min.Should().BeGreaterThan(0.0);
		logScale.Max.Should().BeGreaterThan(logScale.Min);
	}

	[Fact]
	public void Compute_HiddenPen_MarksAxisNotVisible()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, AxisKey: "a", IsVisible: false) };
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (0.0, 10.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		scales.Should().ContainSingle().Which.IsVisible.Should().BeFalse();
	}

	[Fact]
	public void Compute_AutoModeIgnoresNaNGapColumns()
	{
		var model = new PenScaleModel();
		var settings = new[] { new PenScaleSettings(PenId: 1, AxisKey: "a") };

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
		var settings = new[] { new PenScaleSettings(PenId: 1, AxisKey: "a") };
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
			new PenScaleSettings(PenId: 1, AxisKey: "a", Mode: ScaleMode.Manual, ManualMin: 90.0, ManualMax: 10.0)
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
				PenId: 1, AxisKey: "a", Mode: ScaleMode.Manual, ManualMin: -5.0, ManualMax: 100.0, IsLogarithmic: true)
		};
		var envelopes = new Dictionary<int, PenHistoryEnvelope> { [1] = Envelope(1, (1.0, 50.0)) };

		var scales = model.Compute(settings, envelopes, activePenId: 1, _origin, _origin.AddHours(1));

		var manual = scales.Should().ContainSingle().Which;
		manual.Min.Should().BeGreaterThan(0.0);
		manual.Max.Should().Be(100.0);
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
