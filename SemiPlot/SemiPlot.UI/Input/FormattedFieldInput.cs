using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Input;

/// <summary>Which characters a formatted field keeps and where it puts its fixed ones.</summary>
public enum FieldLayout
{
	/// <summary>The <see cref="WallClockRangeForm.TimeFieldFormat"/> slots, filled from the left.</summary>
	ClockTime,

	/// <summary>The <see cref="Resources.ExportDateFormat"/> slots, filled from the left.</summary>
	Date,

	/// <summary><c>h:mm:ss</c> with unbounded hours, grouped from the right.</summary>
	Duration,

	/// <summary><c>#RRGGBB</c>: up to six upper-cased hex digits behind a leading <c>#</c>.</summary>
	HexColor
}

/// <summary>
/// Keeps only the characters a text box's <see cref="FieldLayout"/> allows and inserts its fixed ones.
/// </summary>
public static class FormattedFieldInput
{
	private const int GroupLength = 2;
	internal const int HexColorDigitCount = 6;

	public static readonly AttachedProperty<FieldLayout?> LayoutProperty =
		AvaloniaProperty.RegisterAttached<TextBox, FieldLayout?>("Layout", typeof(FormattedFieldInput));

	/// <summary>
	/// Set by an operator route into a <see cref="FieldLayout.HexColor"/> box, read by its next text change.
	/// </summary>
	private static readonly AttachedProperty<bool> _operatorEditPendingProperty =
		AvaloniaProperty.RegisterAttached<TextBox, bool>("OperatorEditPending", typeof(FormattedFieldInput));

	static FormattedFieldInput()
	{
		LayoutProperty.Changed.AddClassHandler<TextBox>(OnLayoutChanged);
	}

	public static FieldLayout? GetLayout(TextBox textBox)
	{
		return textBox.GetValue(LayoutProperty);
	}

	public static void SetLayout(TextBox textBox, FieldLayout? layout)
	{
		textBox.SetValue(LayoutProperty, layout);
	}

	private static void OnLayoutChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs change)
	{
		textBox.TextChanged -= OnTextChanged;
		RemoveOperatorRoutes(textBox);

		if (change.GetNewValue<FieldLayout?>() is not { } layout)
		{
			return;
		}

		textBox.TextChanged += OnTextChanged;

		if (RuleFor(layout).NormalisesOperatorEditsOnly)
		{
			AddOperatorRoutes(textBox);
		}
	}

	private static void AddOperatorRoutes(TextBox textBox)
	{
		textBox.AddHandler(InputElement.TextInputEvent, MarkOperatorEdit, RoutingStrategies.Tunnel);
		textBox.AddHandler(InputElement.KeyDownEvent, OnOperatorKeyDown, RoutingStrategies.Tunnel);
		textBox.PastingFromClipboard += MarkOperatorEdit;
		textBox.CuttingToClipboard += MarkOperatorEdit;
		textBox.LostFocus += ClearOperatorEdit;
	}

	private static void RemoveOperatorRoutes(TextBox textBox)
	{
		textBox.RemoveHandler(InputElement.TextInputEvent, MarkOperatorEdit);
		textBox.RemoveHandler(InputElement.KeyDownEvent, OnOperatorKeyDown);
		textBox.PastingFromClipboard -= MarkOperatorEdit;
		textBox.CuttingToClipboard -= MarkOperatorEdit;
		textBox.LostFocus -= ClearOperatorEdit;
		textBox.ClearValue(_operatorEditPendingProperty);
	}

	/// <summary>Tunnels so that a paste or cut the key raises inside the box's own handling keeps its mark.</summary>
	private static void OnOperatorKeyDown(object? sender, KeyEventArgs e)
	{
		if (sender is TextBox textBox)
		{
			textBox.SetValue(_operatorEditPendingProperty, KeyChangesText(textBox, e));
		}
	}

	private static bool KeyChangesText(TextBox textBox, KeyEventArgs e)
	{
		var hasSelection = textBox.SelectionStart != textBox.SelectionEnd;
		var hotkeys = Application.Current?.PlatformSettings?.HotkeyConfiguration;

		return e.Key switch
		{
			Key.Back => hasSelection || textBox.CaretIndex > 0,
			Key.Delete => hasSelection || textBox.CaretIndex < (textBox.Text?.Length ?? 0),
			_ => hotkeys is not null
				&& ((textBox.CanUndo && hotkeys.Undo.Any(gesture => gesture.Matches(e)))
					|| (textBox.CanRedo && hotkeys.Redo.Any(gesture => gesture.Matches(e))))
		};
	}

	private static void MarkOperatorEdit(object? sender, RoutedEventArgs e)
	{
		(sender as TextBox)?.SetValue(_operatorEditPendingProperty, true);
	}

	private static void ClearOperatorEdit(object? sender, RoutedEventArgs e)
	{
		(sender as TextBox)?.ClearValue(_operatorEditPendingProperty);
	}

	private static void OnTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (sender is not TextBox textBox || GetLayout(textBox) is not { } layout)
		{
			return;
		}

		var rule = RuleFor(layout);

		if (rule.NormalisesOperatorEditsOnly && !TakeOperatorEdit(textBox))
		{
			return;
		}

		if (textBox.Text is not { } text)
		{
			return;
		}

		var normalised = rule.Normalise(new string([.. text.Where(rule.IsAllowed)]));

		if (normalised == text)
		{
			return;
		}

		var allowedBeforeCaret = text[..Math.Min(textBox.CaretIndex, text.Length)].Count(rule.IsAllowed);
		textBox.SetCurrentValue(TextBox.TextProperty, normalised);
		textBox.SetCurrentValue(
			TextBox.CaretIndexProperty,
			IndexAfterAllowed(normalised, allowedBeforeCaret, rule.IsAllowed));
	}

	private static bool TakeOperatorEdit(TextBox textBox)
	{
		var pending = textBox.GetValue(_operatorEditPendingProperty);
		textBox.ClearValue(_operatorEditPendingProperty);

		return pending;
	}

	private static LayoutRule RuleFor(FieldLayout layout)
	{
		return layout switch
		{
			FieldLayout.ClockTime => new LayoutRule(
				char.IsAsciiDigit,
				digits => FillSlots(digits, WallClockRangeForm.TimeFieldFormat),
				NormalisesOperatorEditsOnly: false),
			FieldLayout.Date => new LayoutRule(
				char.IsAsciiDigit,
				digits => FillSlots(digits, Resources.ExportDateFormat),
				NormalisesOperatorEditsOnly: false),
			FieldLayout.Duration => new LayoutRule(
				char.IsAsciiDigit,
				GroupFromRight,
				NormalisesOperatorEditsOnly: false),
			FieldLayout.HexColor => new LayoutRule(
				char.IsAsciiHexDigit,
				PrefixHash,
				NormalisesOperatorEditsOnly: true),
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

	private static string PrefixHash(string hexDigits)
	{
		var kept = hexDigits[..Math.Min(hexDigits.Length, HexColorDigitCount)].ToUpperInvariant();

		return kept.Length == 0 ? string.Empty : $"#{kept}";
	}

	/// <summary>
	/// The index after the <paramref name="count"/>-th allowed character, or before the first one for zero.
	/// </summary>
	private static int IndexAfterAllowed(string text, int count, Func<char, bool> isAllowed)
	{
		var seen = 0;

		for (var index = 0; index < text.Length; index++)
		{
			if (!isAllowed(text[index]))
			{
				continue;
			}

			if (count == 0)
			{
				return index;
			}

			if (++seen == count)
			{
				return index + 1;
			}
		}

		return text.Length;
	}

	/// <summary>What a layout keeps, how it rebuilds the kept text, and whether a code write is left as set.</summary>
	private sealed record LayoutRule(
		Func<char, bool> IsAllowed,
		Func<string, string> Normalise,
		bool NormalisesOperatorEditsOnly);
}
