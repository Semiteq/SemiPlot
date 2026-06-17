namespace SemiPlot.Core.Trends;

// Renderer-agnostic axis/scale model: from each pen's envelope, scale settings, the active pen, and the
// visible X window it derives one PenScale per axis key (pens sharing a key share a range). Pure data,
// no renderer. Logarithmic axes drop non-positive values before computing the range.
public sealed class PenScaleModel
{
	private const double AutoPaddingFraction = 0.05;
	private const double FlatRangePadding = 0.5;
	private const double LogFallbackMin = 1.0;
	private const double LogFallbackMax = 10.0;

	public IReadOnlyList<PenScale> Compute(
		IReadOnlyList<PenScaleSettings> settings,
		IReadOnlyDictionary<long, PenHistoryEnvelope> envelopes,
		long activePenId,
		DateTime windowStart,
		DateTime windowEnd)
	{
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(envelopes);

		var axisOrder = new List<string>();
		var axisGroups = new Dictionary<string, List<PenScaleSettings>>();

		foreach (var setting in settings)
		{
			if (!axisGroups.TryGetValue(setting.AxisKey, out var members))
			{
				members = new List<PenScaleSettings>();
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
		IReadOnlyDictionary<long, PenHistoryEnvelope> envelopes,
		long activePenId,
		DateTime windowStart,
		DateTime windowEnd)
	{
		var penIds = members.Select(member => member.PenId).ToArray();
		var isActive = members.Any(member => member.PenId == activePenId);
		var isVisible = members.Any(member => member.IsVisible);
		var isLogarithmic = members.Any(member => member.IsLogarithmic);
		var mode = members[0].Mode;

		var range = ComputeRange(members, envelopes, windowStart, windowEnd, mode, isLogarithmic);

		return new PenScale(axisKey, penIds, range.Min, range.Max, mode, isActive, isVisible, isLogarithmic);
	}

	private static (double Min, double Max) ComputeRange(
		IReadOnlyList<PenScaleSettings> members,
		IReadOnlyDictionary<long, PenHistoryEnvelope> envelopes,
		DateTime windowStart,
		DateTime windowEnd,
		ScaleMode mode,
		bool isLogarithmic)
	{
		if (mode == ScaleMode.Manual)
		{
			return SanitizeManualRange(members[0], isLogarithmic);
		}

		var values = CollectValues(members, envelopes, mode, windowStart, windowEnd, isLogarithmic);
		if (values.Count == 0)
		{
			return DefaultRange(isLogarithmic);
		}

		var min = values.Min();
		var max = values.Max();
		return PadRange(min, max, isLogarithmic);
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

	private static List<double> CollectValues(
		IReadOnlyList<PenScaleSettings> members,
		IReadOnlyDictionary<long, PenHistoryEnvelope> envelopes,
		ScaleMode mode,
		DateTime windowStart,
		DateTime windowEnd,
		bool isLogarithmic)
	{
		var values = new List<double>();
		foreach (var member in members)
		{
			if (!envelopes.TryGetValue(member.PenId, out var envelope))
			{
				continue;
			}

			AppendEnvelopeValues(values, envelope, mode, windowStart, windowEnd, isLogarithmic);
		}

		return values;
	}

	private static void AppendEnvelopeValues(
		List<double> values,
		PenHistoryEnvelope envelope,
		ScaleMode mode,
		DateTime windowStart,
		DateTime windowEnd,
		bool isLogarithmic)
	{
		for (var index = 0; index < envelope.Timestamps.Count; index++)
		{
			if (mode == ScaleMode.AutoscaleToWindow && !IsInWindow(envelope.Timestamps[index], windowStart, windowEnd))
			{
				continue;
			}

			AppendIfUsable(values, envelope.Min[index], isLogarithmic);
			AppendIfUsable(values, envelope.Max[index], isLogarithmic);
		}
	}

	private static void AppendIfUsable(List<double> values, double value, bool isLogarithmic)
	{
		if (double.IsNaN(value))
		{
			return;
		}

		if (isLogarithmic && value <= 0.0)
		{
			return;
		}

		values.Add(value);
	}

	private static bool IsInWindow(DateTime timestamp, DateTime windowStart, DateTime windowEnd)
	{
		return timestamp >= windowStart && timestamp <= windowEnd;
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
