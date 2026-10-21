using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>The dialog as the operator drives it: values read off realised controls, input typed and clicked.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class SettingsViewTests : IDisposable
{
	private const string InvalidClass = "invalid";

	private readonly TemporaryDirectory _configDirectory = new("semiplot-settings-view");

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
		_configDirectory.Dispose();
	}

	[AvaloniaFact]
	public void TheRealisedDialog_ShowsTheLoadedValues()
	{
		using var viewModel = Build();
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
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
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var host = Named<TextBox>(dialog, "SettingsHost");
			save.IsEffectivelyEnabled.Should().BeTrue();

			HeadlessInput.Clear(dialog, host);

			host.Text.Should().BeEmpty();
			save.IsEffectivelyEnabled.Should().BeFalse("a blank host cannot be saved");

			HeadlessInput.Type(dialog, host, "10.20.30.40");

			viewModel.Host.Should().Be("10.20.30.40");
			save.IsEffectivelyEnabled.Should().BeTrue();
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
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var port = Named<NumericUpDown>(dialog, "SettingsPort");
			var textBox = Descendant<TextBox>(port, "PART_TextBox");

			HeadlessInput.Type(dialog, textBox, "abc");
			HeadlessInput.Press(dialog, PhysicalKey.Enter);

			viewModel.Port.Should().Be(5432, "text is not a port");
			port.Value.Should().Be(5432);
			save.IsEffectivelyEnabled.Should().BeTrue();

			HeadlessInput.Clear(dialog, textBox);

			viewModel.Port.Should().BeNull();
			port.Classes.Should().Contain(InvalidClass);
			save.IsEffectivelyEnabled.Should().BeFalse("an empty port cannot be saved");

			HeadlessInput.Clear(dialog, textBox);
			HeadlessInput.Type(dialog, textBox, "5433");

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
	public async Task TheRestartNowButton_AppearsWithTheNoticeAndRunsTheHandedRestart()
	{
		var restarts = 0;
		using var viewModel = Build(() => restarts++);
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			var button = Named<Button>(dialog, "SettingsRestartNow");
			var notice = Named<TextBlock>(dialog, "SettingsRestartNotice");
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var dialogBounds = dialog.Bounds;
			var saveBounds = save.Bounds;
			button.IsEffectivelyVisible.Should().BeFalse();
			notice.IsEffectivelyVisible.Should().BeFalse();

			var other = viewModel.Languages.First(choice => choice.Token != viewModel.SelectedLanguage!.Token);
			HeadlessInput.Pick(dialog, Named<ComboBox>(dialog, "SettingsLanguage"), other);
			viewModel.SelectedLanguage!.Token.Should().Be(other.Token);
			HeadlessInput.Click(dialog, Named<Button>(dialog, "SettingsSaveButton"));
			await HeadlessWait.Until(() => button.IsEffectivelyEnabled);

			button.IsEffectivelyVisible.Should().BeTrue();
			notice.IsEffectivelyVisible.Should().BeTrue();
			notice.Text.Should().Be(Resources.SettingsRestartNotice);
			_messagePanel.Entries.Should().BeEmpty();
			AppSettingsLoader.Load(_appDirectory).Value.Locale.Should().Be(UiLanguage.En);
			dialog.Bounds.Should().Be(dialogBounds, "the notice and its button sit in the reserved line");
			save.Bounds.Should().Be(saveBounds, "the notice never moves the buttons");
			HeadlessInput.Click(dialog, button);
			restarts.Should().Be(1);
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
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			var notice = Named<TextBlock>(dialog, "SettingsRestartNotice");
			var restartNow = Named<Button>(dialog, "SettingsRestartNow");
			var message = Named<TextBlock>(dialog, "SettingsValidationMessage");
			var other = viewModel.Languages.First(choice => choice.Token != viewModel.SelectedLanguage!.Token);
			HeadlessInput.Pick(dialog, Named<ComboBox>(dialog, "SettingsLanguage"), other);
			HeadlessInput.Click(dialog, Named<Button>(dialog, "SettingsSaveButton"));
			await HeadlessWait.Until(() => viewModel.IsRestartPending);

			HeadlessInput.Clear(dialog, Named<TextBox>(dialog, "SettingsHost"));

			notice.IsEffectivelyVisible.Should().BeFalse("one line carries one message");
			restartNow.IsEffectivelyVisible.Should().BeFalse("the button belongs to the notice");
			message.Text.Should().Be(Resources.SettingsHostInvalid);
			message.Bounds.Position.Should().Be(
				notice.Bounds.Position, "the notice and the message share the reserved line");
			message.Bounds.Height.Should().Be(notice.Bounds.Height);
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public async Task AThemeAnotherProcessSaved_MovesTheOpenDialog_SoPickingTheOldThemeWritesIt()
	{
		using var scope = ThemeProbe.ApplyVariant(App.VariantFor(AppThemeVariant.Light));
		using var viewModel = Build();
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			File.WriteAllText(Path.Combine(_appDirectory, "app.yaml"), "locale: ru\ntheme: dark\n");
			Application.Current!.RequestedThemeVariant = App.VariantFor(AppThemeVariant.Dark);
			Dispatcher.UIThread.RunJobs();

			Named<ComboBox>(dialog, "SettingsTheme").SelectedItem
				.Should().BeSameAs(viewModel.Themes.Single(choice => choice.Token == "dark"));

			var light = viewModel.Themes.Single(choice => choice.Token == "light");
			HeadlessInput.Pick(dialog, Named<ComboBox>(dialog, "SettingsTheme"), light);
			await viewModel.SaveCommand.Execute();

			_messagePanel.Entries.Should().BeEmpty();
			AppSettingsLoader.Load(_appDirectory).Value.Theme.Should().Be(AppThemeVariant.Light);
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
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			var host = Named<TextBox>(dialog, "SettingsHost");
			var save = Named<Button>(dialog, "SettingsSaveButton");
			var validBounds = dialog.Bounds;
			var validSaveBounds = save.Bounds;

			HeadlessInput.Clear(dialog, host);
			HeadlessInput.Type(dialog, host, "scada-01");

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
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			var host = Named<TextBox>(dialog, "SettingsHost");
			var port = Descendant<TextBox>(Named<NumericUpDown>(dialog, "SettingsPort"), "PART_TextBox");
			BorderColourOf(host).Should().NotBe(error);
			BorderColourOf(port).Should().NotBe(error);

			HeadlessInput.Clear(dialog, host);
			HeadlessInput.Type(dialog, host, "scada-01");

			host.IsFocused.Should().BeTrue();
			BorderColourOf(host).Should().Be(error, "the invalid border wins over Semi's focus border");

			HeadlessInput.Clear(dialog, port);

			port.IsFocused.Should().BeTrue();
			BorderColourOf(port).Should().Be(error, "the invalid border reaches the number field's text box");
			BorderColourOf(host).Should().Be(error, "the invalid border wins over Semi's resting border");

			HeadlessInput.Type(dialog, port, "5432");

			BorderColourOf(port).Should().NotBe(error);
		}
		finally
		{
			dialog.Close();
		}
	}

	[AvaloniaFact]
	public async Task AnUnreadableSection_IsStatedAndKeepsSaveDisabled()
	{
		var connectionFile = Path.Combine(_configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml");
		File.WriteAllText(connectionFile, "host: [unclosed\n");
		using var viewModel = await SettingsViewModel.OpenAsync(
			_configDirectory, _messagePanel, NullLoggerFactory.Instance, restartApplication: () => { });
		var dialog = Realise(new SettingsDialog { DataContext = viewModel });
		try
		{
			Named<TextBlock>(dialog, "SettingsValidationMessage").Text.Should()
				.Be(Resources.FormatSettingsSectionUnreadable(Resources.SettingsConnectionHeader));
			Named<Button>(dialog, "SettingsSaveButton").IsEffectivelyEnabled.Should().BeFalse();
		}
		finally
		{
			dialog.Close();
		}
	}

	private SettingsViewModel Build(Action? restartApplication = null)
	{
		return SettingsViewModelStand.Build(_configDirectory, _messagePanel, restartApplication);
	}

	private static IEnumerable<TemplatedControl> InvalidFields(SettingsDialog dialog)
	{
		return dialog.GetVisualDescendants().OfType<TemplatedControl>().Where(field => field.Classes.Contains(InvalidClass));
	}

	private static Color BorderColourOf(TextBox textBox)
	{
		var border = Descendant<Border>(textBox, "PART_ContentPresenterBorder");

		return border.BorderBrush.Should().BeAssignableTo<ISolidColorBrush>().Subject.Color;
	}
}
