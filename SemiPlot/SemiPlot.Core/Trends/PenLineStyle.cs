namespace SemiPlot.Core.Trends;

// How a pen's center line connects its samples: analog signals are drawn interpolated (straight
// segments between points), while discrete/stepped signals hold their value until the next sample.
public enum PenLineStyle
{
	Interpolated,
	Stepped
}
