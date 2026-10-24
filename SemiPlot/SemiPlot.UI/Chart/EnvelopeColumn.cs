namespace SemiPlot.UI.Chart;

/// <summary>What a gap column means: the chart's break ahead of a pen's first live value, or any other.</summary>
public enum BreakKind
{
	Ordinary,
	Ahead
}

/// <summary>One decimation column: Min/Max extent and Center at one axis X; Break matters on a gap only.</summary>
public readonly record struct EnvelopeColumn(
	double X,
	double Min,
	double Max,
	double Center,
	BreakKind Break = BreakKind.Ordinary);
