using ReactiveUI;

using SemiPlot.Core.Export;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Export;

public sealed class ExportPenChoice(Pen pen, bool isTicked, bool showId = false) : ReactiveObject
{
	public Pen Pen { get; } = pen;

	public string Label { get; } = showId ? PenNaming.WithId(pen.Name, pen) : pen.Name;

	public bool IsTicked
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = isTicked;

	public static IReadOnlyList<ExportPenChoice> Checklist(IReadOnlyList<(Pen Pen, bool IsTicked)> entries)
	{
		var sharedNames = PenNaming.SharedKeys(entries.Select(entry => entry.Pen), pen => pen.Name);

		return [.. entries.Select(entry => new ExportPenChoice(
			entry.Pen, entry.IsTicked, sharedNames.Contains(entry.Pen.Name)))];
	}
}
