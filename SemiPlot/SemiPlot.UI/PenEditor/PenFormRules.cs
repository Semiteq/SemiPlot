using System.Globalization;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.PenEditor;

/// <summary>The rules a form draft must keep to be written; each returns the rule the draft breaks, or null.</summary>
internal static class PenFormRules
{
	public static string? NameRule(string name)
	{
		return string.IsNullOrWhiteSpace(name) ? Resources.PenFormNameRequired : null;
	}

	public static string? MaskRule(string mask)
	{
		return mask.Length == 0 || PenValueFormat.IsAcceptable(mask) ? null : Resources.PenFormMaskInvalid;
	}

	public static string? ColorRule(string color)
	{
		return PenColorConverters.TryParse(color, out _) ? null : Resources.PenFormColorInvalid;
	}

	public static string? ScaleRule(string min, string max)
	{
		return BoundRule(min) ?? BoundRule(max) ?? PairRule(min, max);
	}

	public static string? BoundRule(string bound)
	{
		return TryReadBound(bound, out _) ? null : Resources.PenFormScaleBoundInvalid;
	}

	/// <summary>Null while either bound is unreadable, which <see cref="BoundRule"/> reports.</summary>
	public static string? PairRule(string min, string max)
	{
		if (!TryReadBound(min, out var minimum) || !TryReadBound(max, out var maximum))
		{
			return null;
		}

		if (minimum.HasValue != maximum.HasValue)
		{
			return Resources.PenFormScaleHalfSet;
		}

		return minimum >= maximum ? Resources.PenFormScaleInverted : null;
	}

	// An empty bound is no bound.
	public static bool TryReadBound(string text, out double? bound)
	{
		bound = null;

		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value)
			|| !double.IsFinite(value))
		{
			return false;
		}

		bound = value;

		return true;
	}
}
