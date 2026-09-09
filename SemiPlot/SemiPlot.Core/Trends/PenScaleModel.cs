namespace SemiPlot.Core.Trends;

public sealed class PenScaleModel
{
	private const double AutoPaddingFraction = 0.05;
	private const double FlatRangePadding = 0.5;
	private const double LogFallbackMin = 1.0;
	private const double LogFallbackMax = 10.0;

	public IReadOnlyList<PenScale> Compute(
		IReadOnlyList<PenScaleSettings> settings,
		IReadOnlyDictionary<int, PenHistoryEnvelope> envelopes,
		int activePenId,
		DateTime windowStart,
		DateTime windowEnd)
	{
		var axisOrder = new List<string>();
		var axisGroups = new Dictionary<string, List<PenScaleSettings>>();

		foreach (var setting in settings)
		{
			if (!axisGroups.TryGetValue(setting.AxisKey, out var members))
			{
				members = [];
				axisGroups[setting.AxisKey] = members;
				axisOrder.Add(setting.AxisKey);
			}

			members.Add(setting);
		}

		var scales = new List<PenScale>(axisOrder.Count);
		foreach (var axisKey in axisOrder)
		{
			scales.Add(BuildAxisScale(axisKey, axisGroups[axisKey], envelopes, activePenId, windowStart, windowEnd));
		}

		return scales;
	}

	private static PenScale BuildAxisScale(
		string axisKey,
		IReadOnlyList<PenScaleSettings> members,
		IReadOnlyDictionary<int, PenHistoryEnvelope> envelopes,
		int activePenId,
		DateTime windowStart,
		DateTime windowEnd)
	{
		var penIds = members.Select(member => member.PenId).ToArray();
		var isActive = members.Any(member => member.PenId == activePenId);
		var isVisible = members.Any(member => member.IsVisible);
		var isLogarithmic = members.Any(member => member.IsLogarithmic);
		var mode = members[0].Mode;

		var (min, max) = ComputeRange(members, envelopes, windowStart, windowEnd, mode, isLogarithmic);

		return new PenScale(axisKey, penIds, min, max, mode, isActive, isVisible, isLogarithmic);
	}

	private static (double Min, double Max) ComputeRange(
		IReadOnlyList<PenScaleSettings> members,
		IReadOnlyDictionary<int, PenHistoryEnvelope> envelopes,
		DateTime windowStart,
		DateTime windowEnd,
		ScaleMode mode,
		bool isLogarithmic)
	{
		if (mode == ScaleMode.Manual)
		{
			return SanitizeManualRange(members[0], isLogarithmic);
		}

		if (TryReadRange(members, envelopes, windowStart, windowEnd, isLogarithmic, out var min, out var max))
		{
			return PadRange(min, max, isLogarithmic);
		}

		// docs/architecture/trend-feature-spec.md, AY-4
		if (TryReadRange(members, envelopes, DateTime.MinValue, DateTime.MaxValue, isLogarithmic, out min, out max))
		{
			return PadRange(min, max, isLogarithmic);
		}

		return DefaultRange(isLogarithmic);
	}

	private static (double Min, double Max) SanitizeManualRange(PenScaleSettings setting, bool isLogarithmic)
	{
		var min = Math.Min(setting.ManualMin, setting.ManualMax);
		var max = Math.Max(setting.ManualMin, setting.ManualMax);

		if (isLogarithmic && min <= 0.0)
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
		IReadOnlyList<PenScaleSettings> members,
		IReadOnlyDictionary<int, PenHistoryEnvelope> envelopes,
		DateTime windowStart,
		DateTime windowEnd,
		bool isLogarithmic,
		out double min,
		out double max)
	{
		min = double.MaxValue;
		max = double.MinValue;
		var hasValue = false;

		foreach (var member in members)
		{
			if (!envelopes.TryGetValue(member.PenId, out var envelope))
			{
				continue;
			}

			hasValue |= ReadEnvelopeRange(envelope, windowStart, windowEnd, isLogarithmic, ref min, ref max);
		}

		return hasValue;
	}

	private static bool ReadEnvelopeRange(
		PenHistoryEnvelope envelope,
		DateTime windowStart,
		DateTime windowEnd,
		bool isLogarithmic,
		ref double min,
		ref double max)
	{
		var timestamps = envelope.Timestamps;
		var hasValue = false;

		for (var index = FirstAtOrAfter(timestamps, windowStart); index < timestamps.Count; index++)
		{
			if (timestamps[index] > windowEnd)
			{
				break;
			}

			hasValue |= Widen(envelope.Min[index], isLogarithmic, ref min, ref max);
			hasValue |= Widen(envelope.Max[index], isLogarithmic, ref min, ref max);
		}

		return hasValue;
	}

	private static bool Widen(double value, bool isLogarithmic, ref double min, ref double max)
	{
		if (double.IsNaN(value) || (isLogarithmic && value <= 0.0))
		{
			return false;
		}

		min = Math.Min(min, value);
		max = Math.Max(max, value);

		return true;
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
		if (min == max)
		{
			return ClampLowerToPositive(min - FlatRangePadding, max + FlatRangePadding, isLogarithmic);
		}

		var padding = (max - min) * AutoPaddingFraction;

		return ClampLowerToPositive(min - padding, max + padding, isLogarithmic);
	}

	// A log axis has no defined range below zero, so a padded lower bound is clamped to keep the
	// minimum positive instead of dipping the auto padding past zero.
	private static (double Min, double Max) ClampLowerToPositive(double min, double max, bool isLogarithmic)
	{
		if (isLogarithmic && min <= 0.0)
		{
			min = Math.Min(LogFallbackMin, max);
		}

		return (min, max);
	}

	private static (double Min, double Max) DefaultRange(bool isLogarithmic)
	{
		return isLogarithmic ? (LogFallbackMin, LogFallbackMax) : (0.0, 1.0);
	}
}
