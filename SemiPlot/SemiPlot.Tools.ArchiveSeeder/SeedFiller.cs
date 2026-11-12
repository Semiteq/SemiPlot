namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>The rows written per layer, their total, the recipe and operator markers written, and the tag count
/// written when <see cref="SeederOptions.AdminConnectionString"/> was set.</summary>
public sealed record SeedFillResult(
	IReadOnlyDictionary<short, long> RowsPerLayer,
	long RowsWritten,
	int RecipeMarkersWritten,
	int OperatorMarkersWritten,
	int? TagsWritten);

// The row-generation-and-write sequence a seeding run and converge share: SeedAsync and
// Converge.RunAsync both fill an archive from SeederOptions, one algorithm in one place.
public static class SeedFiller
{
	public static async Task<SeedFillResult> FillAsync(SeederOptions options, CancellationToken cancellationToken = default)
	{
		var rowsPerLayer = new SortedDictionary<short, long>();

		var written = await new ArchiveWriter(options.ConnectionString)
			.WriteAsync(Counted(Rows(options), rowsPerLayer), options.Start, options.End,
				cancellationToken: cancellationToken);

		// After the archive has committed: a marker points into rows that exist.
		var recipeMarkers = ShiftMarkerWriter.RecipePlan(options);
		var operatorMarkers = ShiftMarkerWriter.OperatorPlan(options);

		if (recipeMarkers.Count + operatorMarkers.Count > 0)
		{
			await new ShiftMarkerWriter(options.ConnectionString, TimeZoneInfo.Local)
				.WriteAsync([.. recipeMarkers, .. operatorMarkers], cancellationToken);
		}

		var tags = options.AdminConnectionString is null
			? (int?)null
			: await new TagCatalogWriter(options.AdminConnectionString)
				.WriteAsync(RawLayerGenerator.SelectPens(options.PenCount), cancellationToken);

		return new SeedFillResult(rowsPerLayer, written, recipeMarkers.Count, operatorMarkers.Count, tags);
	}

	/// <summary>The rows one fill writes, generated a calendar day at a time: the day's raw rows, then its
	/// coarse rows.</summary>
	public static IEnumerable<ArchiveRow> Rows(SeederOptions options)
	{
		return DayByDay(RawLayerGenerator.GenerateDays(options));
	}

	private static IEnumerable<ArchiveRow> DayByDay(IEnumerable<IReadOnlyList<ArchiveRow>> days)
	{
		foreach (var day in days)
		{
			foreach (var row in day)
			{
				yield return row;
			}

			foreach (var row in LayerThinner.ThinAll(day))
			{
				yield return row;
			}
		}
	}

	private static IEnumerable<ArchiveRow> Counted(
		IEnumerable<ArchiveRow> rows,
		SortedDictionary<short, long> rowsPerLayer)
	{
		foreach (var row in rows)
		{
			rowsPerLayer[row.Layer] = rowsPerLayer.GetValueOrDefault(row.Layer) + 1;

			yield return row;
		}
	}
}
