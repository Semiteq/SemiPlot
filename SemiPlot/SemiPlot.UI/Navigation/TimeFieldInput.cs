using System.Text;

using Avalonia;
using Avalonia.Controls;

using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Navigation;

/// <summary>Where a time or date field puts its separators among the digits the operator types.</summary>
public enum TimeFieldLayout
{
	/// <summary>The <see cref="WallClockRangeForm.TimeFieldFormat"/> slots, filled from the left.</summary>
	ClockTime,

	/// <summary>The <see cref="Resources.ExportDateFormat"/> slots, filled from the left.</summary>
	Date,

	/// <summary><c>h:mm:ss</c> with unbounded hours, grouped from the right.</summary>
	Duration
}

/// <summary>Keeps only the digits of a text box and inserts the separators of its <see cref="TimeFieldLayout"/>.</summary>
public static class TimeFieldInput
{
	private const int GroupLength = 2;

	public static readonly AttachedProperty<TimeFieldLayout?> LayoutProperty =
		AvaloniaProperty.RegisterAttached<TextBox, TimeFieldLayout?>("Layout", typeof(TimeFieldInput));

	static TimeFieldInput()
	{
		LayoutProperty.Changed.AddClassHandler<TextBox>(OnLayoutChanged);
	}

	public static TimeFieldLayout? GetLayout(TextBox textBox)
	{
		return textBox.GetValue(LayoutProperty);
	}

	public static void SetLayout(TextBox textBox, TimeFieldLayout? layout)
	{
		textBox.SetValue(LayoutProperty, layout);
	}

	private static void OnLayoutChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs change)
	{
		textBox.TextChanged -= OnTextChanged;

		if (change.GetNewValue<TimeFieldLayout?>() is not null)
		{
			textBox.TextChanged += OnTextChanged;
		}
	}

	private static void OnTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (sender is not TextBox { Text: { } text } textBox || GetLayout(textBox) is not { } layout)
		{
			return;
		}

		var grouped = Group(text, layout);

		if (grouped == text)
		{
			return;
		}

		var digitsBeforeCaret = text[..Math.Min(textBox.CaretIndex, text.Length)].Count(char.IsAsciiDigit);
		textBox.SetCurrentValue(TextBox.TextProperty, grouped);
		textBox.SetCurrentValue(TextBox.CaretIndexProperty, IndexAfterDigits(grouped, digitsBeforeCaret));
	}

	private static string Group(string text, TimeFieldLayout layout)
	{
		var digits = new string([.. text.Where(char.IsAsciiDigit)]);

		return layout switch
		{
			TimeFieldLayout.ClockTime => FillSlots(digits, WallClockRangeForm.TimeFieldFormat),
			TimeFieldLayout.Date => FillSlots(digits, Resources.ExportDateFormat),
			TimeFieldLayout.Duration => GroupFromRight(digits),
			_ => throw new ArgumentOutOfRangeException(nameof(layout), layout, null)
		};
	}

	/// <summary>Puts one digit into each letter of <paramref name="format"/> and stops at the last digit.</summary>
	private static string FillSlots(string digits, string format)
	{
		var filled = new StringBuilder(format.Length);
		var next = 0;

		foreach (var slot in format)
		{
			if (next == digits.Length)
			{
				break;
			}

			filled.Append(char.IsAsciiLetter(slot) ? digits[next++] : slot);
		}

		return filled.ToString();
	}

	private static string GroupFromRight(string digits)
	{
		if (digits.Length <= GroupLength)
		{
			return digits;
		}

		var seconds = digits[^GroupLength..];
		var rest = digits[..^GroupLength];

		return rest.Length <= GroupLength
			? $"{rest}:{seconds}"
			: $"{rest[..^GroupLength]}:{rest[^GroupLength..]}:{seconds}";
	}

	private static int IndexAfterDigits(string text, int digitCount)
	{
		if (digitCount == 0)
		{
			return 0;
		}

		var seen = 0;

		for (var index = 0; index < text.Length; index++)
		{
			if (char.IsAsciiDigit(text[index]) && ++seen == digitCount)
			{
				return index + 1;
			}
		}

		return text.Length;
	}
}
