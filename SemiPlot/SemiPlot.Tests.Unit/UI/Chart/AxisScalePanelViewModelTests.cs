using System.ComponentModel;
using System.Globalization;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
[Collection(ProcessGlobalStateCollection.Name)]
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
		chart.AxisScale.IsMinimumValid.Should().BeTrue();
		chart.AxisScale.IsMaximumValid.Should().BeTrue();
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
	public void CommitBounds_WithBothFieldsUntouched_KeepsTheExactBoundsAMaskRounded()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { Format = "0" })]);
		chart.SetAxisLimits(1, 0.4, 9.6);
		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText.Should().Be("0");
		chart.AxisScale.MaximumText.Should().Be("10");

		chart.AxisScale.CommitBounds();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((0.4, 9.6));
	}

	[AvaloniaFact]
	public void CommitBounds_OnAnAutoscaledPenWithUntouchedFields_WritesNothing()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();

		chart.AxisScale.CommitBounds();

		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1));
	}

	[AvaloniaFact]
	public void CommitBounds_WithTheSeededValuesRetypedInAnotherNotation_WritesNothing()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();
		var (minimum, maximum) = chart.ScaleRangeForPen(1)!.Value;

		chart.AxisScale.MinimumText = minimum.ToString("E16", CultureInfo.CurrentCulture);
		chart.AxisScale.MaximumText = maximum.ToString("E16", CultureInfo.CurrentCulture);
		chart.AxisScale.MaximumText.Should().NotBe(PenValueFormat.Format(maximum, null));
		chart.AxisScale.CommitBounds();

		chart.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto, "the typed pair is the pair the panel was seeded with");
	}

	[AvaloniaFact]
	public void CommitBounds_WithOneFieldEdited_WritesTheTypedValueAndKeepsTheOtherExact()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { Format = "0" })]);
		chart.SetAxisLimits(1, 0.4, 9.6);
		chart.AxisScale.Seed();

		chart.AxisScale.MaximumText = "20";
		chart.AxisScale.CommitBounds();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((0.4, 20.0));
	}

	[AvaloniaFact]
	public void AMouseGestureOnThePanelsPen_ClosesThePanel_SoNoSeededBoundUndoesIt()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.ScaleActivePen(0.8, 0.5);
		var settingsAfterGesture = chart.ScaleSettings[1];
		chart.AxisScale.MaximumText = "200";
		chart.AxisScale.CommitBounds();

		closes.Should().Be(1);
		chart.ScaleSettings[1].Should().Be(settingsAfterGesture, "a closed panel writes nothing");
	}

	[AvaloniaFact]
	public void CommitBounds_WritesAChangedPairOnThePenThePanelWasOpenedFor_AndReseedsWithThePanelOpen()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetActivePen(2);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MaximumText = "120.0";
		chart.AxisScale.MinimumText = "-20.00";
		chart.AxisScale.CommitBounds();

		chart.ScaleSettings[2].Should().Be(new PenScaleSettings(2)
		{
			Mode = ScaleMode.Manual,
			ManualMin = -20.0,
			ManualMax = 120.0
		});
		chart.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto, "the other pen is not touched");
		chart.AxisScale.MaximumText.Should().Be("120", "the write re-seeds the fields");
		chart.AxisScale.MinimumText.Should().Be("-20");
		closes.Should().Be(0);
	}

	[AvaloniaFact]
	public void ReportFailure_ReachesTheChartsMessagePanel()
	{
		using var messagePanel = new MessagePanelViewModel();
		using var chart = CreateViewModelOnOneScheduler(messagePanel: messagePanel).ViewModel;

		var thrown = new InvalidOperationException("the focus handler threw");

		chart.AxisScale.ReportFailure(thrown);

		messagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));
	}

	[AvaloniaTheory]
	[InlineData("95")]
	[InlineData("90")]
	public void AnInvertedOrEqualPair_IsRefusedOnTheMessageLine_AndNothingIsWritten(string typedMinimum)
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MinimumText = typedMinimum;

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleMinimumBelowMaximum);
		chart.AxisScale.IsMinimumValid.Should().BeFalse();
		chart.AxisScale.IsMaximumValid.Should().BeFalse();

		chart.AxisScale.CommitBounds();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
		closes.Should().Be(0);
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

		chart.AxisScale.CommitBounds();

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
		chart.AxisScale.IsMinimumValid.Should().BeTrue();

		chart.AxisScale.MinimumText = string.Empty;

		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleBoundsRequired);
		chart.AxisScale.IsMinimumValid.Should().BeFalse();
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

		chart.AxisScale.CommitBounds();

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
		chart.AxisScale.CommitBounds();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((1.5, 150.5));
	}

	[AvaloniaFact]
	public void Cancel_WritesNothingAndAsksToClose_AndTheCommitOnDetachWritesNothing()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 10.0, 90.0);
		chart.AxisScale.Seed();
		chart.AxisScale.MaximumText = "500";
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.CancelCommand.Execute().Subscribe();
		chart.AxisScale.CommitBounds();

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
		chart.AxisScale.CommitBounds();
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
	public void CommitBounds_LeavesTheNavigationWindowAndDeltaCursorsUntouched()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();
		var fromBefore = chart.Navigation.From;
		var toBefore = chart.Navigation.To;

		chart.AxisScale.MaximumText = "42";
		chart.AxisScale.MinimumText = "1";
		chart.AxisScale.CommitBounds();

		chart.Navigation.From.Should().Be(fromBefore);
		chart.Navigation.To.Should().Be(toBefore);
		chart.DeltaFirstCursor.Should().BeNull();
		chart.DeltaSecondCursor.Should().BeNull();
		chart.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void Seed_ReadsTheAxisTypeFromTheChartsSettings()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with { LogScaleOnStart = pen.PenId == 2 })]);

		chart.AxisScale.Seed();
		chart.AxisScale.IsLogarithmic.Should().BeFalse();

		chart.SetActivePen(2);
		chart.AxisScale.Seed();
		chart.AxisScale.IsLogarithmic.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ToggleLogarithmic_WritesThePendingPairFlipsTheFlagAndReseedsWithoutClosing()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 0.5, 200.0);
		chart.AxisScale.Seed();
		chart.AxisScale.MaximumText = "500.0";
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			IsLogarithmic = true,
			ManualMin = 0.5,
			ManualMax = 500.0
		});
		chart.AxisScale.IsLogarithmic.Should().BeTrue();
		chart.AxisScale.MaximumText.Should().Be("500", "the switch re-seeds the bounds");
		chart.AxisScale.ValidationMessage.Should().BeEmpty();
		closes.Should().Be(0);

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.ScaleSettings[1].IsLogarithmic.Should().BeFalse();
		chart.AxisScale.IsLogarithmic.Should().BeFalse();
		closes.Should().Be(0);
	}

	[AvaloniaFact]
	public void ARefusedTick_DropsItsMessageOnceTheMinimumIsRetyped()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 0.0, 100.0);
		chart.AxisScale.Seed();
		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();
		chart.AxisScale.ValidationMessage.Should().Be(Resources.ScaleLogMinimumPositive);

		chart.AxisScale.MinimumText = PenFormRules.FormatBound(5.0);

		chart.AxisScale.ValidationMessage.Should().BeEmpty("the typed minimum is what the operator applies next");
		chart.AxisScale.IsMinimumValid.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TypedOne_ThenToggleOn_WritesTheMinimumAndSwitchesTheAxisOn()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 0.0, 100.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MinimumText = "1";
		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			IsLogarithmic = true,
			ManualMin = 1.0,
			ManualMax = 100.0
		});
		chart.AxisScale.IsLogarithmic.Should().BeTrue();
		chart.AxisScale.ValidationMessage.Should().BeEmpty();
		closes.Should().Be(0);
	}

	[AvaloniaFact]
	public void TypedZero_ThenToggleOff_SwitchesTheAxisOffAndWritesZeroUnderTheLinearRule()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 1.0, 100.0);
		chart.SetLogarithmic(1, true);
		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText = "0";
		chart.AxisScale.ValidationMessage.Should().Be(Resources.ScaleLogMinimumPositive);
		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			ManualMin = 0.0,
			ManualMax = 100.0
		});
		chart.AxisScale.IsLogarithmic.Should().BeFalse();
		chart.AxisScale.MinimumText.Should().Be("0");
		chart.AxisScale.ValidationMessage.Should().BeEmpty();
	}

	[AvaloniaTheory]
	[InlineData("0")]
	[InlineData("-5")]
	[InlineData(null)]
	public void AMinimumTheLogRuleRefuses_ThenToggleOn_IsWrittenOnTheLinearAxisAndTheRuleIsNamed(string? typed)
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, typed is null ? 0.0 : 5.0, 100.0);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		if (typed is not null)
		{
			chart.AxisScale.MinimumText = typed;
		}

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			ManualMin = typed is null ? 0.0 : double.Parse(typed, CultureInfo.InvariantCulture),
			ManualMax = 100.0
		});
		chart.AxisScale.IsLogarithmic.Should().BeFalse();
		chart.AxisScale.MinimumText.Should().Be(typed ?? "0");
		chart.AxisScale.ValidationMessage.Should().Be(Resources.ScaleLogMinimumPositive);
		chart.AxisScale.IsMinimumValid.Should().BeTrue(
			"a non-positive minimum is valid on the linear axis it stays on");
		closes.Should().Be(0);

		chart.AxisScale.Seed();

		chart.AxisScale.ValidationMessage.Should().BeEmpty("a new seed clears the refusal");
	}

	[AvaloniaTheory]
	[InlineData("abc", false)]
	[InlineData("abc", true)]
	public void APairThatDoesNotRead_RefusesTheToggle_AndKeepsTheText(string typed, bool startsLogarithmic)
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 1.0, 100.0);
		chart.SetLogarithmic(1, startsLogarithmic);
		chart.AxisScale.Seed();
		chart.AxisScale.MinimumText = typed;
		var raised = new List<string?>();
		chart.AxisScale.PropertyChanged += Record;

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.AxisScale.PropertyChanged -= Record;
		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			IsLogarithmic = startsLogarithmic,
			ManualMin = 1.0,
			ManualMax = 100.0
		});
		chart.AxisScale.IsLogarithmic.Should().Be(startsLogarithmic);
		chart.AxisScale.MinimumText.Should().Be(typed);
		chart.AxisScale.ValidationMessage.Should().Be(Resources.AxisScaleBoundInvalid);
		raised.Should().Contain(
			nameof(AxisScalePanelViewModel.IsLogarithmic),
			"the box ticked itself and must re-read");

		void Record(object? sender, PropertyChangedEventArgs change)
		{
			raised.Add(change.PropertyName);
		}
	}

	[AvaloniaFact]
	public void ALogPenWhoseMinimumTheMaskShowsAsZero_StaysValidAndKeepsTheExactBound()
	{
		using var culture = new CultureScope("en-US");
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with
		{
			Format = "0.000",
			ScaleMinOnStart = 1e-6,
			ScaleMaxOnStart = 1e-1,
			LogScaleOnStart = true
		})]);

		chart.AxisScale.Seed();

		chart.AxisScale.MinimumText.Should().Be("0.000");
		chart.AxisScale.IsLogarithmic.Should().BeTrue();
		chart.AxisScale.IsMinimumValid.Should().BeTrue();
		chart.AxisScale.ValidationMessage.Should().BeEmpty();

		chart.AxisScale.CommitBounds();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((1e-6, 1e-1));
	}

	[AvaloniaFact]
	public void ToggleLogarithmic_SwitchesTheRenderedAxisToDecadesAndBack()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 1e-6, 1e-1);
		chart.AxisScale.Seed();

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();
		chart.Plot.RenderInMemory(600, 400);

		chart.ActivePenAxis!.TickGenerator.Should().BeOfType<LogTickGenerator>();
		chart.ActivePenAxis.Range.Min.Should().BeApproximately(-6.0, 1e-9);
		chart.ActivePenAxis.Range.Max.Should().BeApproximately(-1.0, 1e-9);

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();
		chart.Plot.RenderInMemory(600, 400);

		chart.ActivePenAxis.TickGenerator.Should().NotBeOfType<LogTickGenerator>();
		chart.ActivePenAxis.Range.Min.Should().Be(1e-6);
		chart.ActivePenAxis.Range.Max.Should().Be(1e-1);
	}

	[AvaloniaFact]
	public void ToggleLogarithmic_OnAnAutoscaledPen_Succeeds()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.AxisScale.Seed();

		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();

		chart.ScaleSettings[1].Should().Be(new PenScaleSettings(1) { IsLogarithmic = true });
		chart.AxisScale.IsLogarithmic.Should().BeTrue();
		chart.AxisScale.ValidationMessage.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void InitialScale_RestoresTheStoredAxisType()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue([.. provider.Pens.Select(pen => pen with
		{
			ScaleMinOnStart = 1e-6,
			ScaleMaxOnStart = 1e-1,
			LogScaleOnStart = true
		})]);
		chart.AxisScale.Seed();
		chart.AxisScale.ToggleLogarithmicCommand.Execute().Subscribe();
		chart.ScaleSettings[1].IsLogarithmic.Should().BeFalse();

		chart.AxisScale.InitialScaleCommand.Execute().Subscribe();

		chart.ScaleSettings[1].IsLogarithmic.Should().BeTrue();
		chart.ScaleRangeForPen(1)!.Value.Should().Be((1e-6, 1e-1));
	}

	[AvaloniaFact]
	public void WithTheFlagOn_AMinimumOfZero_IsRefusedAndNotWritten()
	{
		var (chart, _, _, provider) = CreateViewModel();
		chart.ApplyCatalogue(provider.Pens);
		chart.SetAxisLimits(1, 1.0, 100.0);
		chart.SetLogarithmic(1, true);
		chart.AxisScale.Seed();
		var closes = 0;
		using var subscription = chart.AxisScale.CloseRequests.Subscribe(_ => closes++);

		chart.AxisScale.MinimumText = "0";

		chart.AxisScale.ValidationMessage.Should().Be(Resources.ScaleLogMinimumPositive);
		chart.AxisScale.IsMinimumValid.Should().BeFalse();
		chart.AxisScale.IsMaximumValid.Should().BeTrue();

		chart.AxisScale.CommitBounds();

		chart.ScaleRangeForPen(1)!.Value.Should().Be((1.0, 100.0));
		chart.ScaleSettings[1].IsLogarithmic.Should().BeTrue();
		closes.Should().Be(0);
	}
}
