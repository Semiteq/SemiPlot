using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>
/// The operator's mouse and keyboard on a realised top level, each gesture followed by the jobs it queued.
/// </summary>
internal static class HeadlessInput
{
	internal static void Click(TopLevel topLevel, Control control)
	{
		Click(topLevel, control, new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
	}

	/// <summary>Clicks <paramref name="controlPoint"/>, a point in <paramref name="control"/>'s own coordinates.</summary>
	internal static void Click(TopLevel topLevel, Control control, Point controlPoint)
	{
		var target = control.TranslatePoint(controlPoint, topLevel)
			?? throw new InvalidOperationException("The control is not in the top level's visual tree.");

		topLevel.MouseDown(target, MouseButton.Left);
		topLevel.MouseUp(target, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
	}

	internal static void Clear(TopLevel topLevel, TextBox textBox)
	{
		Click(topLevel, textBox);
		textBox.IsFocused.Should().BeTrue("a click on the field gives it the keyboard");

		topLevel.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
		topLevel.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
		topLevel.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
		topLevel.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
	}

	internal static void Type(TopLevel topLevel, TextBox textBox, string text)
	{
		Click(topLevel, textBox);
		topLevel.KeyTextInput(text);
		Dispatcher.UIThread.RunJobs();
	}

	internal static void Press(TopLevel topLevel, PhysicalKey key)
	{
		topLevel.KeyPressQwerty(key, RawInputModifiers.None);
		topLevel.KeyReleaseQwerty(key, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
	}
}
