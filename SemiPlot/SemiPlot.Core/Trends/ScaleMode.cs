namespace SemiPlot.Core.Trends;

// How a pen's (or shared group's) Y range is determined.
public enum ScaleMode
{
	// Fit the full envelope with padding so values are never flush to the top or bottom edge.
	Auto,

	// Use fixed limits supplied by the operator; data is not consulted.
	Manual,

	// Fit only the values that fall inside the currently visible X window.
	AutoscaleToWindow
}
