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
		var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)
			?? throw new InvalidOperationException("The control is not in the top level's visual tree.");

		topLevel.MouseDown(center, MouseButton.Left);
		topLevel.MouseUp(center, MouseButton.Left);
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
