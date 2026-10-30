using SemiPlot.Core.Data;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// One <c>semiplot_tags</c> row as stored, ahead of either projection: the raw <c>line_style</c> smallint and
/// the raw column strings survive here, because <see cref="StoredPen"/> has already lost the raw line style.
/// </summary>
internal sealed record RawPenRow(
	int Id,
	string Name,
	string? Unit,
	string? Format,
	string? Color,
	short LineStyle,
	bool EnabledOnStart,
	double? ScaleMinOnStart,
	double? ScaleMaxOnStart,
	bool LogScaleOnStart);
