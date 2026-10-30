using System.Collections.Concurrent;
using System.Globalization;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Projects the raw catalogue rows onto both consumers' shapes: the stored catalogue the editor edits and the
/// chart's normalised pens.
/// </summary>
internal sealed class CatalogueNormaliser(ILogger logger)
{
	private const string UncommissionedPenColor = "#808080";

	// docs/architecture/data-integration.md#the-catalogue-while-the-viewer-runs
	// Written from whichever pool thread a catalogue read resumes on.
	private readonly ConcurrentDictionary<(int PenId, int Column, string? StoredValue), byte> _warnedNormalisations =
		new();

	/// <summary>The stored list keeps id order; the pens sort by name and then by id.</summary>
	public CatalogueRead Project(IReadOnlyList<RawPenRow> rows, IReadOnlyList<StoredGroup> groups)
	{
		var namesByPen = GroupNamesByPen(groups);
		var stored = new List<StoredPen>(rows.Count);
		var pens = new List<Pen>(rows.Count);

		foreach (var row in rows)
		{
			var lineStyle = LineStyleOf(row);

			stored.Add(StoredPenOf(row, lineStyle));
			pens.Add(PenOf(row, lineStyle, namesByPen.TryGetValue(row.Id, out var names) ? names : []));
		}

		return new CatalogueRead(
			new PenCatalogue(stored, groups),
			[.. pens.OrderBy(pen => pen.Name, StringComparer.CurrentCulture).ThenBy(pen => pen.PenId)]);
	}

	/// <summary>The stored projection alone; an unreadable line style warns at the editor's fixed level.</summary>
	public static IReadOnlyList<StoredPen> ProjectStoredPens(IReadOnlyList<RawPenRow> rows, ILogger logger)
	{
		return
		[
			.. rows.Select(row => StoredPenOf(
				row,
				StoredLineStyle.Read(row.LineStyle, row.Id, logger, static () => LogLevel.Warning)))
		];
	}

	// The stored groups arrive in name order, so a pen's names read in that order.
	private static Dictionary<int, List<string>> GroupNamesByPen(IReadOnlyList<StoredGroup> groups)
	{
		var namesByPen = new Dictionary<int, List<string>>();

		foreach (var group in groups)
		{
			foreach (var penId in group.MemberPenIds)
			{
				if (!namesByPen.TryGetValue(penId, out var names))
				{
					names = [];
					namesByPen[penId] = names;
				}

				names.Add(group.Name);
			}
		}

		return namesByPen;
	}

	private static StoredPen StoredPenOf(RawPenRow row, PenLineStyle lineStyle)
	{
		return new StoredPen(
			row.Id,
			row.Name,
			row.Unit,
			row.Format,
			row.Color,
			lineStyle,
			row.EnabledOnStart,
			row.ScaleMinOnStart,
			row.ScaleMaxOnStart,
			row.LogScaleOnStart);
	}

	private Pen PenOf(RawPenRow row, PenLineStyle lineStyle, IReadOnlyList<string> groupNames)
	{
		var (scaleMin, scaleMax) = ScalePairOf(row);

		return new Pen(
			row.Id,
			row.Name,
			groupNames,
			ColorOf(row),
			row.Unit,
			FormatOf(row),
			row.EnabledOnStart,
			scaleMin,
			scaleMax,
			row.LogScaleOnStart,
			lineStyle);
	}

	// semiplot_tags_scale_paired forbids a half-set pair, so one reaching here is a hand-edited row on an
	// installation without the constraint: it autoscales rather than opening on a bound with no partner.
	private static (double? Min, double? Max) ScalePairOf(RawPenRow row)
	{
		if (row.ScaleMinOnStart is null || row.ScaleMaxOnStart is null)
		{
			return (null, null);
		}

		return (row.ScaleMinOnStart, row.ScaleMaxOnStart);
	}

	// The server validates a stored colour against the hex pattern, so only its absence is handled here.
	private string ColorOf(RawPenRow row)
	{
		if (row.Color is { } color)
		{
			return color;
		}

		logger.Log(
			NormalisationLevel(row.Id, StoredPenColumn.Color, null),
			"Pen {PenId} carries no colour; it is drawn in {FallbackColor} until one is commissioned.",
			row.Id,
			UncommissionedPenColor);

		return UncommissionedPenColor;
	}

	// No server-side check can parse a .NET mask, so the character rule runs here and an unusable mask
	// leaves the record with none.
	private string? FormatOf(RawPenRow row)
	{
		if (row.Format is not { } storedMask)
		{
			return null;
		}

		if (PenValueFormat.IsAcceptable(storedMask))
		{
			return storedMask;
		}

		logger.Log(
			NormalisationLevel(row.Id, StoredPenColumn.Format, storedMask),
			"Pen {PenId} carries format {StoredMask}, which cannot render a reading; it is drawn under {FallbackMask}.",
			row.Id,
			storedMask,
			PenValueFormat.FallbackMask);

		return null;
	}

	private PenLineStyle LineStyleOf(RawPenRow row)
	{
		return StoredLineStyle.Read(
			row.LineStyle,
			row.Id,
			logger,
			() => NormalisationLevel(
				row.Id,
				StoredPenColumn.LineStyle,
				row.LineStyle.ToString(CultureInfo.InvariantCulture)));
	}

	private LogLevel NormalisationLevel(int penId, int column, string? storedValue)
	{
		return _warnedNormalisations.TryAdd((penId, column, storedValue), 0) ? LogLevel.Warning : LogLevel.Debug;
	}
}
