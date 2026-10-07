namespace SemiPlot.Core.Trends;

public sealed class PenScaleModel
{
	private const double AutoPaddingFraction = 0.05;
	private const double FlatRangePadding = 0.5;
	private const double FlatLogRangeDecades = 0.5;
	private const double LogFallbackMin = 1.0;
	private const double LogFallbackMax = 10.0;
	// 10 to either power is a finite, normal double.
	private const double LowestLogDecade = -307.0;
	private const double HighestLogDecade = 308.0;

	public IReadOnlyList<PenScale> Compute(
		IReadOnlyList<PenScaleSettings> settings,
		IReadOnlyDictionary<int, PenHistoryEnvelope> envelopes,
		int activePenId,
		DateTime windowStart,
		DateTime windowEnd)
	{
		var scales = new List<PenScale>(settings.Count);

		foreach (var setting in settings)
		{
			var (min, max) = ComputeRange(setting, envelopes, windowStart, windowEnd);

			scales.Add(new PenScale(
				setting.PenId,
				min,
				max,
				setting.Mode,
				setting.PenId == activePenId,
				setting.IsLogarithmic));
		}

		return scales;
	}

	private static (double Min, double Max) ComputeRange(
		PenScaleSettings setting,
		IReadOnlyDictionary<int, PenHistoryEnvelope> envelopes,
		DateTime windowStart,
		DateTime windowEnd)
	{
		var isLogarithmic = setting.IsLogarithmic;

		if (setting.Mode == ScaleMode.Manual)
		{
			return SanitizeManualRange(setting, isLogarithmic);
		}

		if (!envelopes.TryGetValue(setting.PenId, out var envelope))
		{
			return DefaultRange(isLogarithmic);
		}

		if (TryReadRange(envelope, windowStart, windowEnd, isLogarithmic, out var min, out var max))
		{
			return PadRange(min, max, isLogarithmic);
		}

		// docs/architecture/trend-feature-spec.md, AY-4
		if (TryReadRange(envelope, DateTime.MinValue, DateTime.MaxValue, isLogarithmic, out min, out max))
		{
			return PadRange(min, max, isLogarithmic);
		}

		return DefaultRange(isLogarithmic);
	}

	private static (double Min, double Max) SanitizeManualRange(PenScaleSettings setting, bool isLogarithmic)
	{
		if (!double.IsFinite(setting.ManualMin) || !double.IsFinite(setting.ManualMax))
		{
			return DefaultRange(isLogarithmic);
		}

		var min = Math.Min(setting.ManualMin, setting.ManualMax);
		var max = Math.Max(setting.ManualMin, setting.ManualMax);

		if (isLogarithmic && !LogAxis.AdmitsMinimum(min))
		{
			min = max > 0.0 ? Math.Min(LogFallbackMin, max) : LogFallbackMin;
			if (max <= min)
			{
				max = min * LogFallbackMax;
			}
		}

		return (min, max);
	}

	// Runs once per mouse move over an envelope carrying three visible windows of columns.
	private static bool TryReadRange(
		PenHistoryEnvelope envelope,
		DateTime windowStart,
		DateTime windowEnd,
		bool isLogarithmic,
		out double min,
		out double max)
	{
		min = double.MaxValue;
		max = double.MinValue;

		var timestamps = envelope.Timestamps;
		var hasValue = false;

		for (var index = FirstAtOrAfter(timestamps, windowStart); index < timestamps.Count; index++)
		{
			if (timestamps[index] > windowEnd)
			{
				break;
			}

			hasValue |= ValueRange.Widen(envelope.Min[index], isLogarithmic, ref min, ref max);
			hasValue |= ValueRange.Widen(envelope.Max[index], isLogarithmic, ref min, ref max);
		}

		return hasValue;
	}

	// PenHistoryEnvelope enforces strictly ascending timestamps.
	private static int FirstAtOrAfter(IReadOnlyList<DateTime> timestamps, DateTime windowStart)
	{
		var low = 0;
		var high = timestamps.Count;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (timestamps[middle] < windowStart)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}

	private static (double Min, double Max) PadRange(double min, double max, bool isLogarithmic)
	{
		if (isLogarithmic)
		{
			return PadLogRange(min, max);
		}

		var padding = min == max ? FlatRangePadding : (max - min) * AutoPaddingFraction;

		return (min - padding, max + padding);
	}

	// docs/architecture/charting.md#log10-y-axis
	private static (double Min, double Max) PadLogRange(double min, double max)
	{
		var lowerDecade = Math.Clamp(Math.Log10(min), LowestLogDecade, HighestLogDecade);
		var upperDecade = Math.Clamp(Math.Log10(max), LowestLogDecade, HighestLogDecade);
		var padding = lowerDecade == upperDecade
			? FlatLogRangeDecades
			: (upperDecade - lowerDecade) * AutoPaddingFraction;

		return (
			Math.Pow(10.0, Math.Max(lowerDecade - padding, LowestLogDecade)),
			Math.Pow(10.0, Math.Min(upperDecade + padding, HighestLogDecade)));
	}

	private static (double Min, double Max) DefaultRange(bool isLogarithmic)
	{
		return isLogarithmic ? (LogFallbackMin, LogFallbackMax) : (0.0, 1.0);
	}
}
