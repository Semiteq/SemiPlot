namespace SemiPlot.UI.Chart;

// The outcome of routing a left-button press, in the order the view's OnPointerPressed evaluates it:
// an axis-region edit (or autoscale) pre-empts everything, then delta-cursor placement when in delta
// mode, otherwise a hand-pan.
public enum ChartPressAction
{
	Pan,
	PlaceDeltaCursor,
	EditAxisBound,
	AutoscaleAxis
}
