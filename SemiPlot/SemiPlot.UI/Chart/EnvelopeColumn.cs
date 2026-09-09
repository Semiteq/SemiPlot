namespace SemiPlot.UI.Chart;

/// <summary>One decimation column: its Min/Max extent and Center at one axis X.</summary>
public readonly record struct EnvelopeColumn(double X, double Min, double Max, double Center);
