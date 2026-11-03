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
	/// <summary>One height for a pen, group and membership row, so each virtualised list estimates its extent.</summary>
	public const double RowHeight = 28;

	public const double PenPaneWidth = 420;

	public const double GroupsPaneWidth = 340;

	public const double FilterBoxWidth = 280;

	public const double DesignedWidth = 1440;

	/// <summary>The pen list's first three columns beside both panes at their widths.</summary>
	public const double MinimumWidth = 1272;

	public const double DesignedHeight = 720;

	/// <summary>Every fixed row of the window with four rows showing in each list.</summary>
	public const double MinimumHeight = 536;

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

	/// <summary>The designed width where the owner's working area holds it, never below the minimum.</summary>
	public static double OpeningWidthFor(double workingAreaWidth)
	{
		return Math.Max(MinimumWidth, Math.Min(DesignedWidth, workingAreaWidth));
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

		FormColorPicker.TemplateApplied += OnColorPickerTemplateApplied;
		AttachColorFlyout();

		GroupRename.DataContextChanged += OnGroupRenameDataContextChanged;
	}

	protected override void OnUnloaded(RoutedEventArgs e)
	{
		RemoveHandler(PointerPressedEvent, EndEditOnPressOutsideFocusable);

		_scaleFocus?.Dispose();
		_scaleFocus = null;

		FormColorPicker.TemplateApplied -= OnColorPickerTemplateApplied;
		DetachColorFlyout();

		GroupRename.DataContextChanged -= OnGroupRenameDataContextChanged;

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
				await scaleForm.EndEditAsync(PenField.ScaleOnStart);
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
				await scaleForm.EndEditAsync(PenField.ScaleOnStart);
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
				await scaleForm.EndEditAsync(PenField.ScaleOnStart);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	private void OnPenListSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (DataContext is PenEditorViewModel editor)
		{
			editor.SelectedRow = PenList.SelectedItem as PenRowViewModel;
		}
	}

	// The focus waits for the block's layout and for the row menu, which hands the focus back to its button as it closes.
	private void OnGroupRenameDataContextChanged(object? sender, EventArgs e)
	{
		if (GroupRename.DataContext is PenGroupViewModel)
		{
			Dispatcher.UIThread.Post(FocusRename, DispatcherPriority.Background);
		}
	}

	private void FocusRename()
	{
		if (GroupRename.Focus())
		{
			GroupRename.SelectAll();
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

	private async void OnStartFlagChanged(object? sender, RoutedEventArgs e)
	{
		try
		{
			if (sender is not CheckBox { IsChecked: { } isChecked, DataContext: PenFormViewModel form } box)
			{
				return;
			}

			if (box == FormEnabledOnStart && isChecked != form.EnabledOnStart)
			{
				await form.ChooseEnabledOnStartAsync(isChecked);
			}
			else if (box == FormLogScaleOnStart && isChecked != form.LogScaleOnStart)
			{
				await form.ChooseLogScaleOnStartAsync(isChecked);
			}
		}
		catch (Exception exception)
		{
			ReportFailure(exception);
		}
	}

	// The picker applies its template only once the form shows, after the window has loaded.
	private void OnColorPickerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
	{
		AttachColorFlyout();
	}

	private void AttachColorFlyout()
	{
		DetachColorFlyout();

		_colorFlyout = FormColorPicker.GetVisualDescendants().OfType<DropDownButton>().FirstOrDefault()?.Flyout;
		if (_colorFlyout is not null)
		{
			_colorFlyout.Opened += OnColorFlyoutOpened;
			_colorFlyout.Closed += OnColorFlyoutClosed;
		}
	}

	private void DetachColorFlyout()
	{
		if (_colorFlyout is not null)
		{
			_colorFlyout.Opened -= OnColorFlyoutOpened;
			_colorFlyout.Closed -= OnColorFlyoutClosed;
			_colorFlyout = null;
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
