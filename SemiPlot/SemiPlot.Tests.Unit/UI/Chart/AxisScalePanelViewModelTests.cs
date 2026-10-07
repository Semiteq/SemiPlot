using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class AxisScalePanelViewModelTests
{
	public enum PanelPenLeaves
	{
		TheChart,
		Visibility,
		ActiveStatus
	}

	[AvaloniaFact]
	public void Seed_FillsTheFieldsAndTheHeaderFromTheActivePen()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen.PenId == 2 ? pen with { Unit = "sccm" } : pen)]);
		chart.SetAxisLimits(1, 1.0, 2.0);
		chart.SetAxisLimits(2, 10.0, 90.5);
		chart.SetActivePen(2);

		chart.AxisScale.Seed().Should().BeTrue();

		chart.AxisScale.PenName.Should().Be("Pen 2");
		chart.AxisScale.PenUnit.Should().Be("sccm");
		chart.AxisScale.MaximumText.Should().Be(PenFormRules.FormatBound(90.5));
		chart.AxisScale.MinimumText.Should().Be(PenFormRules.FormatBound(10.0));
		chart.AxisScale.IsValid.Should().BeTrue();
		chart.AxisScale.ValidationMessage.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void Seed_ForAPenWithNoUnit_LeavesTheUnitEmpty()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);

		chart.AxisScale.Seed().Should().BeTrue();

		chart.AxisScale.PenName.Should().Be("Pen 1");
		chart.AxisScale.PenUnit.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void Seed_WithNoPens_OpensNothing()
	{
		var (chart, _, _, _) = CreateViewModel();

		chart.AxisScale.Seed().Should().BeFalse();
	}

	[AvaloniaFact]
	public void Seed_WhenTheActivePenIsHidden_OpensNothing()
	{
		var (chart, _, _, _) = CreateViewModel();
		chart.ApplyCatalogue([new Pen(1, "Pen 1", [], "#ff0000")]);
		chart.SetPenVisibility(1, false);

		chart.AxisScale.Seed().Should().BeFalse();
	}

	[AvaloniaFact]
	public void ASmallMagnitudeScale_IsShownAndWrittenBackWithoutLoss()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 1e-7, 5e-6);

		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText.Should().Be(PenValueFormat.Format(1e-7, null));
		chart.AxisScale.MaximumText.Should().Be(PenValueFormat.Format(5e-6, null));

		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((1e-7, 5e-6));
	}

	[AvaloniaFact]
	public void Seed_FormatsTheBoundsUnderThePensMask()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { Format = "0.0" })]);
		chart.SetAxisLimits(1, 0.25, 43.50529834);

		chart.AxisScale.Seed();

		chart.AxisScale.MaximumText.Should().Be("43.5");
		chart.AxisScale.MinimumText.Should().Be("0.3");
	}

	[AvaloniaFact]
	public void Seed_WithoutAMask_FallsBackToTheDefaultMask()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 43.50529834);

		chart.AxisScale.Seed();

		chart.AxisScale.MaximumText.Should().Be("43.505");
		chart.AxisScale.MinimumText.Should().Be("10");
	}

	[AvaloniaFact]
	public void Apply_WithBothFieldsUntouched_KeepsTheExactBoundsAMaskRounded()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { Format = "0" })]);
		chart.SetAxisLimits(1, 0.4, 9.6);
		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText.Should().Be("0");
		chart.AxisScale.MaximumText.Should().Be("10");

		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((0.4, 9.6));
	}

	[AvaloniaFact]
	public void Apply_WithOneFieldEdited_WritesTheTypedValueAndKeepsTheOtherExact()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { Format = "0" })]);
		chart.SetAxisLimits(1, 0.4, 9.6);
		chart.AxisScale.Seed();

		chart.AxisScale.MaximumText = "20";
		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((0.4, 20.0));
	}

	[AvaloniaFact]
	public void Apply_WritesBothBoundsAsAManualScaleOnThePenThePanelWasOpenedFor()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetActivePen(2);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MaximumText = PenFormRules.FormatBound(120.0);
		chart.AxisScale.MinimumText = PenFormRules.FormatBound(-20.0);
		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleSettings[2].Should().Be(new PenScaleSettings(2)
		{
			Mode = ScaleMode.Manual,
			ManualMin = -20.0,
			ManualMax = 120.0
		});
		chart.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto, "the other pen is not touched");
		closes.Should().Be(1);
	}

	[AvaloniaFact]
	public void AnInvertedPair_IsRefusedOnTheMessageLine_AndNothingIsWritten()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MinimumText = "95";

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleMinimumBelowMaximum);
		chart.AxisScale.IsMinimumValid.Should().BeFalse();
		chart.AxisScale.IsMaximumValid.Should().BeFalse();
		CanApply(chart.AxisScale).Should().BeFalse();

		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		closes.Should().Be(0);
	}

	[AvaloniaFact]
	public void AnEqualPair_IsRefused()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText = "90";

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleMinimumBelowMaximum);
		CanApply(chart.AxisScale).Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public void AnEmptyField_IsRefusedAndMarkedInvalid(bool emptyMaximum)
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		if (emptyMaximum)
		{
			chart.AxisScale.MaximumText = string.Empty;
		}
		else
		{
			chart.AxisScale.MinimumText = " ";
		}

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleBoundsRequired);
		chart.AxisScale.IsMaximumValid.Should().Be(!emptyMaximum);
		chart.AxisScale.IsMinimumValid.Should().Be(emptyMaximum);
		CanApply(chart.AxisScale).Should().BeFalse();

		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		closes.Should().Be(0);
	}

	[AvaloniaFact]
	public void ABoundThatAMaskShowsAsBlank_StaysValidWithNoMessageWhileAsSeeded()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { Format = "0.0;-0.0; " })]);
		chart.SetAxisLimits(1, 0.0, 100.0);

		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText.Should().Be(" ");
		chart.AxisScale.ValidationMessage.Should().BeEmpty();
		chart.AxisScale.IsValid.Should().BeTrue();
		chart.AxisScale.IsMinimumValid.Should().BeTrue();
		CanApply(chart.AxisScale).Should().BeTrue();

		chart.AxisScale.MinimumText = string.Empty;

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleBoundsRequired);
		chart.AxisScale.IsMinimumValid.Should().BeFalse();
		CanApply(chart.AxisScale).Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData("en-US", "1x")]
	[InlineData("en-US", "1,5")]
	[InlineData("ru-RU", "150.5")]
	[InlineData("en-US", "NaN")]
	[InlineData("en-US", "Infinity")]
	public void AnUnreadableField_IsRefusedAndNothingIsWritten(string cultureName, string typed)
	{
		using var culture = new CultureScope(cultureName);
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MaximumText = typed;

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleBoundInvalid);
		chart.AxisScale.IsMaximumValid.Should().BeFalse();
		chart.AxisScale.IsMinimumValid.Should().BeTrue();
		CanApply(chart.AxisScale).Should().BeFalse();

		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		closes.Should().Be(0);
	}

	[AvaloniaFact]
	public void AFractionInTheCurrentCulturesSeparator_IsWritten()
	{
		using var culture = new CultureScope("ru-RU");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText = "1,5";
		chart.AxisScale.MaximumText = "150,5";
		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((1.5, 150.5));
	}

	[AvaloniaFact]
	public void Cancel_WritesNothingAndAsksToClose()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		chart.AxisScale.MaximumText = "500";
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.CancelCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		closes.Should().Be(1);
	}

	[AvaloniaFact]
	public void Autoscale_ActsOnTheActivePenAndCloses()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.SetAxisLimits(2, 20.0, 80.0);
		chart.SetActivePen(2);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.AutoscaleCommand.Execute().Subscribe();

		chart.ScaleSettings[2].Mode.Should().Be(ScaleMode.Auto);
		chart.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
		closes.Should().Be(1);
	}

	[AvaloniaFact]
	public void InitialScale_ActsOnTheActivePenAndCloses()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(
			[.. provider.Pens.Select(pen => pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 })]);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.SetAxisLimits(2, 20.0, 80.0);
		chart.SetActivePen(2);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.InitialScaleCommand.Execute().Subscribe();

		chart.ScaleRangeForPen(2)!.Value.Should().Be((5.0, 50.0));
		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		closes.Should().Be(1);
	}

	[AvaloniaTheory]
	[InlineData(PanelPenLeaves.TheChart)]
	[InlineData(PanelPenLeaves.Visibility)]
	[InlineData(PanelPenLeaves.ActiveStatus)]
	public void WhenThePenIsNoLongerDrawn_ThePanelClosesAndEveryCommandWritesNothing(PanelPenLeaves leaves)
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(
			[.. provider.Pens.Select(pen => pen with { ScaleMinOnStart = 5.0, ScaleMaxOnStart = 50.0 })]);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.SetAxisLimits(2, 20.0, 80.0);
		chart.SetActivePen(2);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		switch (leaves)
		{
			case PanelPenLeaves.TheChart:
				chart.ApplyCatalogue([provider.Pens[0]]);

				break;

			case PanelPenLeaves.Visibility:
				chart.SetPenVisibility(2, false);

				break;

			default:
				chart.SetActivePen(1);

				break;
		}

		closes.Should().Be(1, "the pen the panel was opened for is no longer the one whose axis is drawn");

		chart.AxisScale.MaximumText = "500";
		chart.AxisScale.ApplyCommand.Execute().Subscribe();
		chart.AxisScale.AutoscaleCommand.Execute().Subscribe();
		chart.AxisScale.InitialScaleCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		if (leaves != PanelPenLeaves.TheChart)
		{
			chart.ScaleSettings[2].Mode.Should().Be(ScaleMode.Manual);
			chart.ScaleRangeForPen(2)!.Value.Should().Be((20.0, 80.0));
		}
	}

	[AvaloniaFact]
	public void Apply_LeavesTheNavigationWindowAndDeltaCursorsUntouched()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();
		var fromBefore = chart.Navigation.From;
		var toBefore = chart.Navigation.To;

		chart.AxisScale.MaximumText = "42";
		chart.AxisScale.MinimumText = "1";
		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.Navigation.From.Should().Be(fromBefore);
		chart.Navigation.To.Should().Be(toBefore);
		chart.DeltaFirstCursor.Should().BeNull();
		chart.DeltaSecondCursor.Should().BeNull();
		chart.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void Apply_PutsBothBoundsOnTheRenderedAxis()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();

		chart.AxisScale.MaximumText = "120";
		chart.AxisScale.MinimumText = "-20";
		chart.AxisScale.ApplyCommand.Execute().Subscribe();

		chart.Plot.RenderInMemory(600, 400);
		chart.ActivePenAxis!.Range.Min.Should().Be(-20.0);
		chart.ActivePenAxis.Range.Max.Should().Be(120.0);
	}

	private static bool CanApply(AxisScalePanelViewModel panel)
	{
		var canExecute = false;
		using var subscription = panel.ApplyCommand.CanExecute.Subscribe(value => canExecute = value);

		return canExecute;
	}
}
