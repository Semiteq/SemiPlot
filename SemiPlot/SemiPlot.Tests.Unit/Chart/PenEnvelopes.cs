using SemiPlot.Core.Trends;

namespace SemiPlot.Tests.Unit.Chart;

/// <summary>A history envelope whose minimum, maximum and centre agree at every column.</summary>
internal static class PenEnvelopes
{
	public static PenHistoryEnvelope Envelope(int penId, params (DateTime Time, double Center)[] columns)
	{
		var timestamps = new DateTime[columns.Length];
		var min = new double[columns.Length];
		var max = new double[columns.Length];
		var center = new double[columns.Length];

		for (var index = 0; index < columns.Length; index++)
		{
			timestamps[index] = columns[index].Time;
			center[index] = columns[index].Center;
			min[index] = columns[index].Center;
			max[index] = columns[index].Center;
		}

		return new PenHistoryEnvelope(penId, timestamps, min, max, center);
	}
}
