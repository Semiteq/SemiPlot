namespace SemiPlot.Core.Trends;

/// <summary>
/// How a pen joins its samples. The values are pinned because they are the wire format of
/// <c>semiplot_tags.line_style</c>, a <c>smallint</c> holding the member's ordinal: reordering the
/// members or inserting one ahead of <see cref="Stepped"/> would silently reinterpret every
/// commissioned site's catalogue, with no compiler error and no failing test outside the gated suite.
/// </summary>
public enum PenLineStyle
{
	Interpolated = 0,
	Stepped = 1
}
