using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using FluentResults;

using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Data.Integration;

// The seeded database every test class clones, built once per run by `semibase create`, then
// ArchiveWriter, then TagCatalogWriter.
public static class ArchiveTemplate
{
	public const string NamePrefix = "semiplot_bench_";

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

	// A discriminator over the seeder assembly, the schema script and the Slice options, so a persistent
	// server cannot serve last week's seed to this week's code.
	public static string Name { get; } = ComputeName();

	public static async Task<Result<string>> BuildAsync(
		PostgresServer postgresServer,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var provisioned = await SemibaseProvisioner.CreateAsync(postgresServer, Name, cancellationToken);

			if (provisioned.IsFailed)
			{
				return Result.Fail<string>($"semibase {SemibaseProvisioner.CreateCommand} failed: "
					+ string.Join("; ", provisioned.Errors.Select(error => error.Message)));
			}

			var seeded = await SeedAsync(postgresServer, cancellationToken);

			return seeded.IsFailed ? Result.Fail<string>(seeded.Errors) : Result.Ok(Name);
		}
		catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException)
		{
			return Result.Fail<string>($"the template database could not be built: {exception.Message}");
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

	// The template is reused when it is already seeded — that is what the discriminator in its name
	// buys. A database that exists but carries no archive is a crashed earlier run, and semibase create
	// has just made it usable again, so seeding it is the repair.
	private static async Task<Result> SeedAsync(
		PostgresServer postgresServer,
		CancellationToken cancellationToken)
	{
		var adminConnectionString = postgresServer.AdminConnectionStringFor(Name);

		if (await ArchiveExistsAsync(adminConnectionString, cancellationToken))
		{
			return Result.Ok();
		}

		var rawRows = RawLayerGenerator.Generate(Slice);
		var rows = rawRows.Concat(LayerThinner.ThinAll(rawRows)).ToArray();

		var written = await new ArchiveWriter(postgresServer.WriterConnectionStringFor(Name))
			.WriteAsync(rows, Slice.Start, Slice.End, cancellationToken);

		if (written.IsFailed)
		{
			return Result.Fail(written.Errors);
		}

		var tags = await new TagCatalogWriter(adminConnectionString)
			.WriteAsync(RawLayerGenerator.SelectPens(Slice.PenCount), cancellationToken);

		return tags.IsFailed ? Result.Fail(tags.Errors) : Result.Ok();
	}

	private static async Task<bool> ArchiveExistsAsync(
		string connectionString,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(ArchiveWriter.ArchiveExistsCommand, connection);

		return await command.ExecuteScalarAsync(cancellationToken) is true;
	}

	private static string ComputeName()
	{
		var generatorVersion = typeof(ArchiveWriter).Assembly.ManifestModule.ModuleVersionId;

		var material = string.Join(
			'|',
			generatorVersion.ToString("N"),
			ArchiveWriter.ReadSchemaScript(),
			string.Format(
				CultureInfo.InvariantCulture,
				"{0}/{1}/{2}/{3}/{4}/{5:O}",
				Slice.Days,
				Slice.PenCount,
				Slice.Seed,
				Slice.ChangeSeconds,
				Slice.BreakCount,
				Slice.End));

		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));

		return NamePrefix + Convert.ToHexStringLower(digest)[..16];
	}
}
