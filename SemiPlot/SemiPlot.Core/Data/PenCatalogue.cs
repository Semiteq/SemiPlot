using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>The catalogue as stored, for the editor: no value is normalised the way the chart's read does.</summary>
public sealed record PenCatalogue(IReadOnlyList<StoredPen> Pens, IReadOnlyList<StoredGroup> Groups);

/// <summary>One <c>semiplot_tags</c> row as stored; a null field is a stored <c>NULL</c>.</summary>
public sealed record StoredPen(
	int Id,
	string Name,
	string? Unit,
	string? Format,
	string? Color,
	PenLineStyle LineStyle,
	bool EnabledOnStart,
	double? ScaleMinOnStart,
	double? ScaleMaxOnStart);

/// <summary>One <c>semiplot_groups</c> row and the ids of its member pens, in ascending order.</summary>
public sealed record StoredGroup(int Id, string Name, IReadOnlyList<int> MemberPenIds);
