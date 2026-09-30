using System.Reactive.Linq;
using System.Runtime.CompilerServices;

using Avalonia.Media;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>
/// The edit form of one pen: a draft per field, written one field at a time when its edit ends. A refused or
/// failed write reverts the draft, marks the field and says why on <see cref="Message"/>.
/// </summary>
public sealed class PenFormViewModel : ReactiveObject, IDisposable
{
	public const double MaskPreviewSample = 1234.5678;

	private readonly IPenCatalogueEditor _penCatalogueEditor;
	private readonly EditorCallQueue _editorCallQueue;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger _logger;
	private readonly IDisposable _rowFollowing;
	private Refusal? _refusal;

	public PenFormViewModel(
		PenRowViewModel row,
		IPenCatalogueEditor penCatalogueEditor,
		EditorCallQueue editorCallQueue,
		MessagePanelViewModel messagePanel,
		ILogger logger)
	{
		Row = row;
		_penCatalogueEditor = penCatalogueEditor;
		_editorCallQueue = editorCallQueue;
		_messagePanel = messagePanel;
		_logger = logger;

		foreach (var penField in Enum.GetValues<PenField>())
		{
			Seed(penField, row.QueuedPen);
		}

		_rowFollowing = row.WhenAnyValue(queued => queued.QueuedPen)
			.Buffer(2, 1)
			.Where(pens => pens.Count == 2)
			.Subscribe(pens => Follow(pens[0], pens[1]));
	}

	public PenRowViewModel Row { get; }

	public string Name
	{
		get;
		set => SetDraft(ref field, value, PenField.Name);
	} = string.Empty;

	public string Unit
	{
		get;
		set => SetDraft(ref field, value, PenField.Unit);
	} = string.Empty;

	public string Mask
	{
		get;
		set => SetDraft(ref field, value, PenField.Mask);
	} = string.Empty;

	public string Color
	{
		get;
		set => SetDraft(ref field, value, PenField.Color);
	} = string.Empty;

	public PenLineStyle LineStyle
	{
		get;
		private set => SetDraft(ref field, value, PenField.LineStyle);
	}

	public bool EnabledOnStart
	{
		get;
		private set => SetDraft(ref field, value, PenField.EnabledOnStart);
	}

	/// <summary>The lower scale bound as the operator types it, read under the current culture.</summary>
	public string ScaleMin
	{
		get;
		set => SetDraft(ref field, value, PenField.Scale);
	} = string.Empty;

	public string ScaleMax
	{
		get;
		set => SetDraft(ref field, value, PenField.Scale);
	} = string.Empty;

	public bool IsNameValid => IsValid(PenField.Name, RuleOf(PenField.Name));

	public bool IsUnitValid => IsValid(PenField.Unit, null);

	public bool IsMaskValid => IsValid(PenField.Mask, RuleOf(PenField.Mask));

	public bool IsColorValid => IsValid(PenField.Color, RuleOf(PenField.Color));

	public bool IsScaleMinValid =>
		IsValid(PenField.Scale, PenFormRules.BoundRule(ScaleMin) ?? PenFormRules.PairRule(ScaleMin, ScaleMax));

	public bool IsScaleMaxValid =>
		IsValid(PenField.Scale, PenFormRules.BoundRule(ScaleMax) ?? PenFormRules.PairRule(ScaleMin, ScaleMax));

	/// <summary>The first rule a draft breaks, in form order; otherwise the last refusal; otherwise empty.</summary>
	public string Message => FirstBrokenRule() ?? _refusal?.Text ?? string.Empty;

	/// <summary>The sample as the chart would draw it under the mask draft, the fallback for an unusable one.</summary>
	public string MaskPreview => PenValueFormat.Format(MaskPreviewSample, Mask);

	/// <summary>
	/// Writes the field's draft when it is valid and differs from the pen as queued; a refused or failed write
	/// reverts the draft and marks the field, but never past a later write of that field still queued.
	/// </summary>
	public async Task EndEditAsync(PenField penField)
	{
		if (RuleOf(penField) is { } brokenRule)
		{
			Refuse(penField, brokenRule);

			return;
		}

		if (ChangeOf(penField) is not { } change)
		{
			return;
		}

		var written = await Row.WriteAsync(
			change,
			() => _editorCallQueue.RunAsync(() => _penCatalogueEditor.ChangeAsync(Row.Pen, change)));

		if (written.IsFailed)
		{
			var refusal = ArchiveFailureMapper.Map(written.Errors[0]).Title;

			if (Row.IsQueued(change))
			{
				Mark(penField, refusal);
			}
			else
			{
				Refuse(penField, refusal);
			}

			_messagePanel.ReportFailure(written, _logger);

			return;
		}

		if (_refusal?.Field == penField)
		{
			_refusal = null;
			RaiseValidity();
		}
	}

	public Task PickColorAsync(Color color)
	{
		Color = PenColorConverters.Format(color);

		return EndEditAsync(PenField.Color);
	}

	public Task ChooseLineStyleAsync(PenLineStyle lineStyle)
	{
		LineStyle = lineStyle;

		return EndEditAsync(PenField.LineStyle);
	}

	public Task ChooseEnabledOnStartAsync(bool enabledOnStart)
	{
		EnabledOnStart = enabledOnStart;

		return EndEditAsync(PenField.EnabledOnStart);
	}

	/// <summary>Stops the drafts following the row; an edit ended afterwards still writes.</summary>
	public void Dispose()
	{
		_rowFollowing.Dispose();
	}

	private void SetDraft<T>(ref T draft, T value, PenField penField, [CallerMemberName] string propertyName = "")
	{
		if (EqualityComparer<T>.Default.Equals(draft, value))
		{
			return;
		}

		draft = value;
		this.RaisePropertyChanged(propertyName);
		OnDraftChanged(penField);
	}

	private void OnDraftChanged(PenField penField)
	{
		if (_refusal?.Field == penField)
		{
			_refusal = null;
		}

		RaiseValidity();
	}

	// The draft goes back first and the mark follows, so the mark survives its own revert.
	private void Refuse(PenField penField, string refusal)
	{
		Revert(penField);
		Mark(penField, refusal);
	}

	private void Mark(PenField penField, string refusal)
	{
		_refusal = new Refusal(penField, refusal);
		RaiseValidity();
	}

	private void Revert(PenField penField)
	{
		Seed(penField, Row.QueuedPen);
	}

	private void Follow(StoredPen before, StoredPen after)
	{
		foreach (var penField in Enum.GetValues<PenField>())
		{
			if (Shows(penField, before))
			{
				Seed(penField, after);
			}
		}
	}

	private void Seed(PenField penField, StoredPen pen)
	{
		switch (penField)
		{
			case PenField.Name:
				Name = pen.Name;
				break;
			case PenField.Unit:
				Unit = TextOf(pen.Unit);
				break;
			case PenField.Mask:
				Mask = TextOf(pen.Format);
				break;
			case PenField.Color:
				Color = TextOf(pen.Color);
				break;
			case PenField.LineStyle:
				LineStyle = pen.LineStyle;
				break;
			case PenField.EnabledOnStart:
				EnabledOnStart = pen.EnabledOnStart;
				break;
			case PenField.Scale:
				ScaleMin = TextOf(pen.ScaleMin);
				ScaleMax = TextOf(pen.ScaleMax);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(penField), penField, null);
		}
	}

	/// <summary>True while the field's draft is the text the pen seeds it with.</summary>
	private bool Shows(PenField penField, StoredPen pen)
	{
		return penField switch
		{
			PenField.Name => Name == pen.Name,
			PenField.Unit => Unit == TextOf(pen.Unit),
			PenField.Mask => Mask == TextOf(pen.Format),
			PenField.Color => Color == TextOf(pen.Color),
			PenField.LineStyle => LineStyle == pen.LineStyle,
			PenField.EnabledOnStart => EnabledOnStart == pen.EnabledOnStart,
			PenField.Scale => ScaleMin == TextOf(pen.ScaleMin) && ScaleMax == TextOf(pen.ScaleMax),
			_ => throw new ArgumentOutOfRangeException(nameof(penField), penField, null)
		};
	}

	private void RaiseValidity()
	{
		this.RaisePropertyChanged(nameof(IsNameValid));
		this.RaisePropertyChanged(nameof(IsUnitValid));
		this.RaisePropertyChanged(nameof(IsMaskValid));
		this.RaisePropertyChanged(nameof(IsColorValid));
		this.RaisePropertyChanged(nameof(IsScaleMinValid));
		this.RaisePropertyChanged(nameof(IsScaleMaxValid));
		this.RaisePropertyChanged(nameof(Message));
		this.RaisePropertyChanged(nameof(MaskPreview));
	}

	private bool IsValid(PenField penField, string? brokenRule)
	{
		return brokenRule is null && _refusal?.Field != penField;
	}

	private string? FirstBrokenRule()
	{
		return RuleOf(PenField.Name) ?? RuleOf(PenField.Mask) ?? RuleOf(PenField.Color) ?? RuleOf(PenField.Scale);
	}

	private string? RuleOf(PenField penField)
	{
		return penField switch
		{
			PenField.Name => PenFormRules.NameRule(Name),
			PenField.Mask => PenFormRules.MaskRule(Mask),
			PenField.Color => PenFormRules.ColorRule(Color),
			PenField.Scale => PenFormRules.ScaleRule(ScaleMin, ScaleMax),
			_ => null
		};
	}

	// Only called on a draft that breaks no rule. The pair compares numbers, so a bound retyped in another
	// notation writes nothing.
	private PenSettingChange? ChangeOf(PenField penField)
	{
		var queued = Row.QueuedPen;

		if (penField == PenField.Scale)
		{
			return ScaleChangeOf(queued);
		}

		if (Shows(penField, queued))
		{
			return null;
		}

		return penField switch
		{
			PenField.Name => new PenSettingChange.Name(Name),
			PenField.Unit => new PenSettingChange.Unit(NullIfEmpty(Unit)),
			PenField.Mask => new PenSettingChange.Format(NullIfEmpty(Mask)),
			PenField.Color => new PenSettingChange.Color(Color),
			PenField.LineStyle => new PenSettingChange.LineStyle(LineStyle),
			PenField.EnabledOnStart => new PenSettingChange.EnabledOnStart(EnabledOnStart),
			_ => throw new ArgumentOutOfRangeException(nameof(penField), penField, null)
		};
	}

	private PenSettingChange.Scale? ScaleChangeOf(StoredPen queued)
	{
		PenFormRules.TryReadBound(ScaleMin, out var min);
		PenFormRules.TryReadBound(ScaleMax, out var max);

		return min == queued.ScaleMin && max == queued.ScaleMax ? null : new PenSettingChange.Scale(min, max);
	}

	private static string TextOf(string? stored)
	{
		return stored ?? string.Empty;
	}

	private static string TextOf(double? stored)
	{
		return stored is { } bound ? PenFormRules.FormatBound(bound) : string.Empty;
	}

	private static string? NullIfEmpty(string draft)
	{
		return draft.Length == 0 ? null : draft;
	}

	private readonly record struct Refusal(PenField Field, string Text);
}
