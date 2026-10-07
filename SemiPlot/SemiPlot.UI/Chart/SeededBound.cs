using SemiPlot.UI.PenEditor;

namespace SemiPlot.UI.Chart;

/// <summary>An axis scale panel field's seed: the text it showed and the exact bound behind that text.</summary>
internal readonly record struct SeededBound(string Text, double Value)
{
	private bool Matches(string text)
	{
		return Text is not null && string.Equals(text, Text, StringComparison.Ordinal);
	}

	public bool IsUnreadable(string text)
	{
		return !Matches(text) && !PenFormRules.TryReadBound(text, out _);
	}

	public bool IsEmpty(string text)
	{
		return !Matches(text) && string.IsNullOrWhiteSpace(text);
	}

	// docs/architecture/trend-interaction.md#the-axis-scale-panel
	public bool TryRead(string text, out double bound)
	{
		bound = 0.0;

		if (Matches(text))
		{
			bound = Value;

			return true;
		}

		if (!PenFormRules.TryReadBound(text, out var read) || read is not { } value)
		{
			return false;
		}

		bound = value;

		return true;
	}
}
