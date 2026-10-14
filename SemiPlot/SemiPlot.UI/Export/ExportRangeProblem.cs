namespace SemiPlot.UI.Export;

/// <summary>The first rule an export window breaks, in the order the form shows them.</summary>
public enum ExportRangeProblem
{
	None,
	StartDateMissing,
	StartUnreadable,
	EndDateMissing,
	EndUnreadable,
	EndNotAfterStart,
	SpanOverLimit,
	NoPenTicked
}
