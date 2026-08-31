namespace SemiPlot.Core.Trends;

/// <summary>
/// How a pen joins its samples. Values are the wire format of <c>semiplot_tags.line_style</c>; do not reorder.
/// </summary>
public enum PenLineStyle
{
	Interpolated = 0,
	Stepped = 1
}
