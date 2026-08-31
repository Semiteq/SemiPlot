using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Data.Integration;

// The seeded database the classes that read the seeded rows clone, built once per run as a clone of the
// provisioned source, then filled by ArchiveWriter and TagCatalogWriter. A class that writes its own rows
// clones the provisioned source instead — CloneSource names which.
//
// It is a clone rather than a provisioning of its own because the image provisions one fixed database
// and this one is a second. CREATE DATABASE ... TEMPLATE carries the table ownership, the relacl and
// the default privileges across; database CONNECT is not carried, and PUBLIC's default already covers
// it.
public static class ArchiveTemplate
{
	public const string Name = "semiplot_bench";

	// The standard slice the whole roadmap develops against. --end is fixed rather than floating, so
	// two runs of the same seed produce the same archive.
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

		var written = await new ArchiveWriter(postgresServer.WriterConnectionStringFor(Name))
			.WriteAsync(rows, Slice.Start, Slice.End, cancellationToken: cancellationToken);

		if (written.IsFailed)
		{
			throw new InvalidOperationException(
				"the template database could not be seeded: "
				+ string.Join("; ", written.Errors.Select(error => error.Message)));
		}

		var tags = await new TagCatalogWriter(adminConnectionString)
			.WriteAsync(RawLayerGenerator.SelectPens(Slice.PenCount), cancellationToken);

		if (tags.IsFailed)
		{
			throw new InvalidOperationException(
				"the template's tag catalogue could not be written: "
				+ string.Join("; ", tags.Errors.Select(error => error.Message)));
		}
	}
}
