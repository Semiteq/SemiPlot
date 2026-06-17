namespace SemiPlot.Core.Trends;

// Operator-controlled scaling configuration for a single pen. AxisKey groups pens that share one Y
// axis (a shared-group scale): pens with the same key are scaled together; a key unique to one pen
// gives that pen its own axis. ManualMin/ManualMax apply only when Mode is Manual. IsLogarithmic
// requests a log range, which drops non-positive values before the range is computed.
public sealed record PenScaleSettings(
	long PenId,
	string AxisKey,
	ScaleMode Mode = ScaleMode.Auto,
	bool IsVisible = true,
	bool IsLogarithmic = false,
	double ManualMin = 0.0,
	double ManualMax = 1.0);
