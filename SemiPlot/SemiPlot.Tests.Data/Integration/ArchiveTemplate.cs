using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Data.Integration;

// The seeded database the classes that read the seeded rows clone, built once per run as a clone of the
// provisioned source, then filled by ArchiveWriter and TagCatalogWriter. A class that writes its own rows
// clones the provisioned source instead — CloneSource names which.
public static class ArchiveTemplate
{
	public const string Name = "semiplot_bench";

	// The standard slice the whole roadmap develops against.
	public static readonly SeederOptions Slice = new(
		string.Empty,
		new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
		SeederOptions.DefaultDays,
		SeederOptions.DefaultPenCount,
		SeederOptions.DefaultSeed,
		SeederOptions.DefaultChangeSeconds,
		SeederOptions.DefaultBreakCount,
		null);

	public static async Task BuildAsync(
		PostgresServer postgresServer,
		SemaphoreSlim creationGate,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await ArchiveDatabase.CopyAsync(
				postgresServer,
				creationGate,
				BenchNames.ProvisionedDatabase,
				Name,
				cancellationToken);

			await SeedAsync(postgresServer, cancellationToken);
		}
		finally
		{
			// CREATE DATABASE ... TEMPLATE refuses while another session holds the source, and every
			// connection opened above is pooled rather than closed.
			ArchiveDatabase.ClearPool(postgresServer.AdminConnectionStringFor(Name));
			ArchiveDatabase.ClearPool(postgresServer.WriterConnectionStringFor(Name));
			ArchiveDatabase.ClearPool(postgresServer.ReaderConnectionStringFor(Name));
		}
	}

	private static async Task SeedAsync(PostgresServer postgresServer, CancellationToken cancellationToken)
	{
		var adminConnectionString = postgresServer.AdminConnectionStringFor(Name);

		var rawRows = RawLayerGenerator.Generate(Slice);
		var rows = rawRows.Concat(LayerThinner.ThinAll(rawRows)).ToArray();

		await new ArchiveWriter(postgresServer.WriterConnectionStringFor(Name))
			.WriteAsync(rows, Slice.Start, Slice.End, cancellationToken: cancellationToken);

		await new TagCatalogWriter(adminConnectionString)
			.WriteAsync(RawLayerGenerator.SelectPens(Slice.PenCount), cancellationToken);
	}
}
