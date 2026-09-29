using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.PenEditor;

/// <summary>
/// The editor window. Its code-behind routes where an edit ends; the view models decide what it writes.
/// </summary>
public partial class PenEditorWindow : Window
{
	/// <summary>One height for every table row, so the virtualised list estimates its extent exactly.</summary>
	public const double RowHeight = 28;

	public static readonly IReadOnlyList<PenLineStyle> LineStyles = Enum.GetValues<PenLineStyle>();

	/// <summary>The pen form field a text box edits, for a box that ends its edit on its own.</summary>
	public static readonly AttachedProperty<PenField?> FieldProperty =
		AvaloniaProperty.RegisterAttached<PenEditorWindow, TextBox, PenField?>("Field");

	private Func<Task>? _textEdit;
	private PenFormViewModel? _scaleForm;
	private IDisposable? _scaleFocus;
	private FlyoutBase? _colorFlyout;
	private PenFormViewModel? _colorForm;
	private Color _colorAtOpen;
	private Task? _drain;

	public PenEditorWindow()
	{
		InitializeComponent();

		AddHandler(GotFocusEvent, OnFieldGotFocus);
		AddHandler(LostFocusEvent, OnFieldLostFocus);
		AddHandler(KeyDownEvent, OnFieldKeyDown, RoutingStrategies.Tunnel);
	}

	public static PenField? GetField(TextBox textBox)
	{
		return textBox.GetValue(FieldProperty);
	}

	public static void SetField(TextBox textBox, PenField? field)
	{
		textBox.SetValue(FieldProperty, field);
	}

	protected override void OnLoaded(RoutedEventArgs e)
	{
		base.OnLoaded(e);

		AddHandler(
			PointerPressedEvent,
			EndEditOnPressOutsideFocusable,
			RoutingStrategies.Tunnel,
			handledEventsToo: true);

		_scaleFocus = FormScale.GetObservable(IsKeyboardFocusWithinProperty)
			.Subscribe(OnScaleFocusWithinChanged);

		_colorFlyout = FormColorPicker.GetVisualDescendants().OfType<DropDownButton>().FirstOrDefault()?.Flyout;
		if (_colorFlyout is not null)
		{
			_colorFlyout.Opened += OnColorFlyoutOpened;
			_colorFlyout.Closed += OnColorFlyoutClosed;
		}
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		RemoveHandler(PointerPressedEvent, EndEditOnPressOutsideFocusable);

		_scaleFocus?.Dispose();
		_scaleFocus = null;

		if (_colorFlyout is not null)
		{
			_colorFlyout.Opened -= OnColorFlyoutOpened;
			_colorFlyout.Closed -= OnColorFlyoutClosed;
			_colorFlyout = null;
		}

		base.OnUnloaded(e);
	}

	// MainWindow disposes the view model after.
	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (_drain is { IsCompleted: true })
		{
			base.OnClosing(e);

			return;
		}

		e.Cancel = true;
		_drain ??= DrainAsync();
	}

	private async Task DrainAsync()
	{
		try
		{
			if (TakeTextEdit() is { } textEdit)
			{
				await textEdit();
			}

			if (TakeScaleForm() is { } scaleForm)
			{
				await scaleForm.EndEditAsync(PenField.Scale);
			}

			if (DataContext is PenEditorViewModel editor)
			{
				await editor.WhenIdleAsync();
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
		finally
		{
			Dispatcher.UIThread.Post(Close);
		}
	}

	private void EndEditOnPressOutsideFocusable(object? sender, PointerPressedEventArgs e)
	{
		if (e.Source is Visual pressed && !IsInsideFocusableControl(pressed))
		{
			FocusManager?.Focus(null);
		}
	}

	private bool IsInsideFocusableControl(Visual pressed)
	{
		return pressed.GetSelfAndVisualAncestors()
			.TakeWhile(visual => visual != this)
			.Any(visual => visual is InputElement { Focusable: true, IsEffectivelyEnabled: true });
	}

	// The edit ends on the view model the field showed when focus entered it, whatever it shows by then.
	private void OnFieldGotFocus(object? sender, FocusChangedEventArgs e)
	{
		if (e.Source is TextBox { DataContext: PenFormViewModel form } textBox && GetField(textBox) is { } field)
		{
			_textEdit = () => form.EndEditAsync(field);
		}
		else if (e.Source == GroupRename && GroupRename.DataContext is PenGroupViewModel group)
		{
			_textEdit = group.EndRenameAsync;
		}
	}

	private async void OnFieldLostFocus(object? sender, FocusChangedEventArgs e)
	{
		try
		{
			if (e.Source is TextBox field && EndsItsEditOnItsOwn(field) && TakeTextEdit() is { } textEdit)
			{
				await textEdit();
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	private async void OnFieldKeyDown(object? sender, KeyEventArgs e)
	{
		try
		{
			if (e.Key != Key.Enter || e.Source is not TextBox field)
			{
				return;
			}

			if (EndsItsEditOnItsOwn(field) && _textEdit is { } textEdit)
			{
				await textEdit();
			}
			else if (_scaleForm is { } scaleForm && field.GetVisualAncestors().Contains(FormScale))
			{
				await scaleForm.EndEditAsync(PenField.Scale);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	// The pair ends its edit only when focus leaves both boxes, so tabbing between them writes no half pair.
	private async void OnScaleFocusWithinChanged(bool isFocusWithin)
	{
		try
		{
			if (isFocusWithin)
			{
				_scaleForm = FormScale.DataContext as PenFormViewModel;
			}
			else if (TakeScaleForm() is { } scaleForm)
			{
				await scaleForm.EndEditAsync(PenField.Scale);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	private async void OnLineStyleSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		try
		{
			if (FormLineStyle is { SelectedItem: PenLineStyle lineStyle, DataContext: PenFormViewModel form }
				&& lineStyle != form.LineStyle)
			{
				await form.ChooseLineStyleAsync(lineStyle);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	private async void OnEnabledOnStartChanged(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (FormEnabledOnStart is { IsChecked: { } enabledOnStart, DataContext: PenFormViewModel form }
				&& enabledOnStart != form.EnabledOnStart)
			{
				await form.ChooseEnabledOnStartAsync(enabledOnStart);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	// The picker only reads the draft, so a flyout opened and dismissed on a pen with no colour writes nothing.
	private void OnColorFlyoutOpened(object? sender, EventArgs e)
	{
		_colorForm = FormColorPicker.DataContext as PenFormViewModel;
		_colorAtOpen = FormColorPicker.Color;
	}

	private async void OnColorFlyoutClosed(object? sender, EventArgs e)
	{
		try
		{
			var form = _colorForm;
			_colorForm = null;
			var picked = FormColorPicker.Color;

			if (form is not null && picked != _colorAtOpen)
			{
				await form.PickColorAsync(picked);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	/// <summary>True for a pen form field outside the scale pair, and for the group rename field.</summary>
	private bool EndsItsEditOnItsOwn(TextBox field)
	{
		return GetField(field) is not null || field == GroupRename;
	}

	private Func<Task>? TakeTextEdit()
	{
		var textEdit = _textEdit;
		_textEdit = null;

		return textEdit;
	}

	private PenFormViewModel? TakeScaleForm()
	{
		var scaleForm = _scaleForm;
		_scaleForm = null;

		return scaleForm;
	}

	private void ReportFailure(Exception failure)
	{
		(DataContext as PenEditorViewModel)?.ReportFailure(failure);
	}
}
