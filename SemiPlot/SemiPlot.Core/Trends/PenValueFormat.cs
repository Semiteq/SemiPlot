using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SemiPlot.Core.Trends;

/// <summary>
/// The .NET numeric mask a pen's value renders under, and the rule that decides whether a stored mask is
/// usable (docs/architecture/charting.md).
/// </summary>
public static partial class PenValueFormat
{
	/// <summary>
	/// The mask a pen with no usable one of its own renders under.
	/// </summary>
	public const string FallbackMask = "0.###";

	// Positive, negative, zero: the three sections .NET reads, and the last index a mask may carry.
	private const int ZeroSectionIndex = 2;

	/// <summary>
	/// Whether the stored mask may render a reading. A character rule rather than a try over
	/// <c>ToString</c>: .NET throws on almost no mask and prints the mistake instead.
	/// </summary>
	public static bool IsAcceptable([NotNullWhen(true)] string? mask)
	{
		if (mask is null || !AcceptableMask().IsMatch(mask))
		{
			return false;
		}

		var remaining = mask.AsSpan();

		for (var section = 0; ; section++)
		{
			if (section > ZeroSectionIndex)
			{
				return false;
			}

			var separator = remaining.IndexOf(';');
			var body = separator < 0 ? remaining : remaining[..separator];

			if (!IsAcceptableSection(body, mustPrintTheReading: section < ZeroSectionIndex))
			{
				return false;
			}

			if (separator < 0)
			{
				return true;
			}

			remaining = remaining[(separator + 1)..];
		}
	}

	/// <summary>
	/// The reading under the pen's mask, or under <see cref="FallbackMask"/> when the mask is unusable.
	/// </summary>
	public static string Format(double value, string? mask)
	{
		return value.ToString(IsAcceptable(mask) ? mask : FallbackMask, CultureInfo.CurrentCulture);
	}

	// The positive and negative sections must carry a 0, which always prints; the third is the zero
	// literal, where a dash standing for "no reading" is the operator's own idiom.
	private static bool IsAcceptableSection(ReadOnlySpan<char> section, bool mustPrintTheReading)
	{
		if (section.IsEmpty)
		{
			return false;
		}

		if (mustPrintTheReading && !section.Contains('0'))
		{
			return false;
		}

		for (var index = 0; index < section.Length; index++)
		{
			// A comma anywhere but between two digit placeholders divides the reading by a thousand per
			// comma instead of grouping it, which is the same class of mistake as %: a wrong number.
			if (section[index] == ','
				&& !(IsDigitPlaceholder(section, index - 1) && IsDigitPlaceholder(section, index + 1)))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsDigitPlaceholder(ReadOnlySpan<char> section, int index)
	{
		return (uint)index < (uint)section.Length && section[index] is '0' or '#';
	}

	// E and e stay in for the exponent of the custom grammar; every other letter is out, so a mask is
	// never a literal the operator reads back as a number. % and per-mille are out because they scale
	// the reading rather than only shaping it.
	[GeneratedRegex(@"^[0#.,;()Ee+\- ]+$")]
	private static partial Regex AcceptableMask();
}
