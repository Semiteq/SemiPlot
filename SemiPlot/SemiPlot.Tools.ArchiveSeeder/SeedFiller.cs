namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>The rows a fill generated, the count written to the archive, and the tag count written when
/// <see cref="SeederOptions.AdminConnectionString"/> was set.</summary>
public sealed record SeedFillResult(ArchiveRow[] Rows, long RowsWritten, int? TagsWritten);

// The row-generation-and-write sequence a seeding run and converge share: SeedAsync and
// Converge.RunAsync both fill an archive from SeederOptions, one algorithm in one place.
public static class SeedFiller
{
	public static async Task<SeedFillResult> FillAsync(SeederOptions options, CancellationToken cancellationToken = default)
	{
		var rawRows = RawLayerGenerator.Generate(options);
		ArchiveRow[] rows = [.. rawRows, .. LayerThinner.ThinAll(rawRows)];

		var written = await new ArchiveWriter(options.ConnectionString)
			.WriteAsync(rows, options.Start, options.End, cancellationToken: cancellationToken);

		var tags = options.AdminConnectionString is null
			? (int?)null
			: await new TagCatalogWriter(options.AdminConnectionString)
				.WriteAsync(RawLayerGenerator.SelectPens(options.PenCount), cancellationToken);

		return new SeedFillResult(rows, written, tags);
	}
}
