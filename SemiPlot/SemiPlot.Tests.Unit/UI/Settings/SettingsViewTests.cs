using System.Diagnostics;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>The dialog as the operator drives it: values read off realised controls, input typed and clicked.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class SettingsViewTests : IDisposable
{
	private const string InvalidClass = "invalid";

	private static readonly TimeSpan _saveTimeout = TimeSpan.FromSeconds(30);

	private readonly string _configDirectory = Directory.CreateTempSubdirectory("semiplot-settings-view-").FullName;

	private readonly string _appDirectory;

	private readonly MessagePanelViewModel _messagePanel = new();

	public SettingsViewTests()
	{
		_appDirectory = Path.Combine(_configDirectory, StartupSequence.SettingsDirectoryName);
		ShippedConfiguration.CopyTo(_configDirectory, "secret");
	}

	public void Dispose()
	{
		_messagePanel.Dispose();
		Directory.Delete(_configDirectory, recursive: true);
	}

	[AvaloniaFact]
	public void TheRealisedDialog_ShowsTheLoadedValues()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			Named<ComboBox>(dialog, "SettingsLanguage").SelectedItem
				.Should().BeSameAs(viewModel.Languages.Single(choice => choice.Token == "ru"));
			Named<ComboBox>(dialog, "SettingsTheme").SelectedItem
				.Should().BeSameAs(viewModel.Themes.Single(choice => choice.Token == "light"));
			Named<TextBox>(dialog, "SettingsHost").Text.Should().Be("127.0.0.1");
			Named<NumericUpDown>(dialog, "SettingsPort").Value.Should().Be(5432);
			Named<NumericUpDown>(dialog, "SettingsPort").Text.Should().Be("5432");
			Named<TextBox>(dialog, "SettingsDatabase").Text.Should().Be("semiplot");
			Named<TextBox>(dialog, "SettingsUser").Text.Should().Be("semiplot");
			Named<TextBox>(dialog, "SettingsPassword").Text.Should().Be("secret");
			Named<NumericUpDown>(dialog, "SettingsPollInterval").Value.Should().Be(1000);
			Named<NumericUpDown>(dialog, "SettingsPollInterval").Text.Should().Be("1000");
			Named<TextBlock>(dialog, "SettingsValidationMessage").Text.Should().BeEmpty();
			InvalidFields(dialog).Should().BeEmpty();
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public void TheSaveButton_IsDisabledWhileARequiredFieldIsBlank()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var host = Named<TextBox>(dialog, "SettingsHost");
			save.IsEffectivelyEnabled.Should().BeTrue();

			Clear(dialog, host);

			host.Text.Should().BeEmpty();
			save.IsEffectivelyEnabled.Should().BeFalse("a blank host cannot be saved");

			Type(dialog, host, "10.20.30.40");

			viewModel.Host.Should().Be("10.20.30.40");
			save.IsEffectivelyEnabled.Should().BeTrue();
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public void TheHostField_RefusesAHostNameAndSaysWhy()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var host = Named<TextBox>(dialog, "SettingsHost");
			var message = Named<TextBlock>(dialog, "SettingsValidationMessage");
			message.Text.Should().BeEmpty();

			Clear(dialog, host);
			Type(dialog, host, "scada-01");

			viewModel.Host.Should().Be("scada-01");
			message.Text.Should().Be(Resources.SettingsHostInvalid);
			host.Classes.Should().Contain(InvalidClass);
			save.IsEffectivelyEnabled.Should().BeFalse("a host name is not an IPv4 address");
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public void ThePortField_RefusesTextAndKeepsTheSaveDisabled()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var port = Named<NumericUpDown>(dialog, "SettingsPort");
			var textBox = Part<TextBox>(port, "PART_TextBox");

			Type(dialog, textBox, "abc");
			Commit(dialog);

			viewModel.Port.Should().Be(5432, "text is not a port");
			port.Value.Should().Be(5432);
			save.IsEffectivelyEnabled.Should().BeTrue();

			Clear(dialog, textBox);

			viewModel.Port.Should().BeNull();
			port.Classes.Should().Contain(InvalidClass);
			save.IsEffectivelyEnabled.Should().BeFalse("an empty port cannot be saved");

			Clear(dialog, textBox);
			Type(dialog, textBox, "5433");

			viewModel.Port.Should().Be(5433);
			port.Classes.Should().NotContain(InvalidClass);
			save.IsEffectivelyEnabled.Should().BeTrue();
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public async Task TheRestartNotice_IsHiddenBeforeASaveAndVisibleAfterOneThatSucceeded()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var notice = Named<TextBlock>(dialog, "SettingsRestartNotice");
			notice.IsEffectivelyVisible.Should().BeFalse();

			var dark = viewModel.Themes.Single(choice => choice.Token == "dark");
			Pick(dialog, Named<ComboBox>(dialog, "SettingsTheme"), dark);
			viewModel.SelectedTheme!.Token.Should().Be("dark");

			Click(dialog, Named<Button>(dialog, "SettingsSaveButton"));
			await WaitUntil(() => viewModel.IsRestartPending);

			notice.IsEffectivelyVisible.Should().BeTrue();
			_messagePanel.Entries.Should().BeEmpty();
			AppSettingsLoader.Load(_appDirectory).Value.Theme.Should().Be(AppThemeVariant.Dark);
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public async Task TheRestartNotice_GivesWayToAMessageInTheSameLine()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var notice = Named<TextBlock>(dialog, "SettingsRestartNotice");
			var message = Named<TextBlock>(dialog, "SettingsValidationMessage");
			var dark = viewModel.Themes.Single(choice => choice.Token == "dark");
			Pick(dialog, Named<ComboBox>(dialog, "SettingsTheme"), dark);
			Click(dialog, Named<Button>(dialog, "SettingsSaveButton"));
			await WaitUntil(() => viewModel.IsRestartPending);

			Clear(dialog, Named<TextBox>(dialog, "SettingsHost"));

			notice.IsEffectivelyVisible.Should().BeFalse("one line carries one message");
			message.Text.Should().Be(Resources.SettingsHostInvalid);
			message.Bounds.Should().Be(notice.Bounds, "the notice and the message share the reserved line");
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public void TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid()
	{
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var host = Named<TextBox>(dialog, "SettingsHost");
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var validBounds = dialog.Bounds;
			var validSaveBounds = save.Bounds;

			Clear(dialog, host);
			Type(dialog, host, "scada-01");

			dialog.Bounds.Should().Be(validBounds, "a message never resizes the form");
			save.Bounds.Should().Be(validSaveBounds, "a message never moves the buttons");
		}
		finally
		{
			dialog.Close();
		}
	}

	// Semi paints the focused border from its own style: docs/architecture/ui-theme.md#a-form-never-resizes-on-validation
	[AvaloniaFact]
	public void AnInvalidField_PaintsItsBorderWithTheErrorBrushFocusedOrNot()
	{
		var variant = App.VariantFor(AppThemeVariant.Light);
		using var scope = ThemeProbe.ApplyVariant(variant);
		var error = ThemeProbe.Colour("AppSeverityErrorBrush", variant);
		using var viewModel = Build();
		var dialog = Realise(viewModel);
		try
		{
			var host = Named<TextBox>(dialog, "SettingsHost");
			var port = Part<TextBox>(Named<NumericUpDown>(dialog, "SettingsPort"), "PART_TextBox");
			BorderColourOf(host).Should().NotBe(error);
			BorderColourOf(port).Should().NotBe(error);

			Clear(dialog, host);
			Type(dialog, host, "scada-01");

			host.IsFocused.Should().BeTrue();
			BorderColourOf(host).Should().Be(error, "the invalid border wins over Semi's focus border");

			Clear(dialog, port);

			port.IsFocused.Should().BeTrue();
			BorderColourOf(port).Should().Be(error, "the invalid border reaches the number field's text box");
			BorderColourOf(host).Should().Be(error, "the invalid border wins over Semi's resting border");

			Type(dialog, port, "5432");

			BorderColourOf(port).Should().NotBe(error);
		}
		finally
		{
			dialog.Close();
		}
	}

	private SettingsViewModel Build()
	{
		var (app, connection) = SettingsSave.ReadOwned(_configDirectory);

		return new SettingsViewModel(
			_configDirectory, app, connection, _messagePanel, NullLogger<SettingsViewModel>.Instance);
	}

	private static SettingsDialog Realise(SettingsViewModel viewModel)
	{
		var dialog = new SettingsDialog { DataContext = viewModel };
		dialog.Show();
		Dispatcher.UIThread.RunJobs();

		return dialog;
	}

	private static T Named<T>(SettingsDialog dialog, string name)
		where T : Control
	{
		return dialog.FindControl<T>(name) ?? throw new InvalidOperationException($"No control named {name}.");
	}

	private static T Part<T>(TemplatedControl owner, string name)
		where T : Control
	{
		return owner.GetVisualDescendants().OfType<T>().FirstOrDefault(child => child.Name == name)
			?? throw new InvalidOperationException($"{owner.GetType().Name} realised no {name}.");
	}

	private static IEnumerable<TemplatedControl> InvalidFields(SettingsDialog dialog)
	{
		return dialog.GetVisualDescendants().OfType<TemplatedControl>().Where(field => field.Classes.Contains(InvalidClass));
	}

	private static Color BorderColourOf(TextBox textBox)
	{
		var border = Part<Border>(textBox, "PART_ContentPresenterBorder");

		return border.BorderBrush.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
	}

	private static void Commit(SettingsDialog dialog)
	{
		dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
		dialog.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Click(TopLevel topLevel, Control control)
	{
		var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)
			?? throw new InvalidOperationException("The control is not in the top level's visual tree.");

		topLevel.MouseDown(center, MouseButton.Left);
		topLevel.MouseUp(center, MouseButton.Left);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Clear(SettingsDialog dialog, TextBox textBox)
	{
		Click(dialog, textBox);
		textBox.IsFocused.Should().BeTrue("a click on the field gives it the keyboard");

		dialog.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);
		dialog.KeyReleaseQwerty(PhysicalKey.A, RawInputModifiers.Control);
		dialog.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
		dialog.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Type(SettingsDialog dialog, TextBox textBox, string text)
	{
		Click(dialog, textBox);
		dialog.KeyTextInput(text);
		Dispatcher.UIThread.RunJobs();
	}

	private static void Pick(SettingsDialog dialog, ComboBox comboBox, SettingsChoice choice)
	{
		Click(dialog, comboBox);
		comboBox.IsDropDownOpen.Should().BeTrue("a click on the box opens its list");

		var item = comboBox.ContainerFromItem(choice)
			?? throw new InvalidOperationException("The open list realised no container for the choice.");
		var popup = TopLevel.GetTopLevel(item)
			?? throw new InvalidOperationException("The open list is not in a top level.");

		Click(popup, item);
		comboBox.IsDropDownOpen.Should().BeFalse("a click on an entry picks it and closes the list");
	}

	private static async Task WaitUntil(Func<bool> condition)
	{
		var clock = Stopwatch.StartNew();

		while (!condition())
		{
			if (clock.Elapsed > _saveTimeout)
			{
				throw new TimeoutException("The save did not complete.");
			}

			await Task.Delay(10);
			Dispatcher.UIThread.RunJobs();
		}
	}
}
