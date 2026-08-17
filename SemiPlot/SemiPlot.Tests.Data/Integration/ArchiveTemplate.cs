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

	// A run stamps the template it is about to use with the server's clock, and the sweep only drops a
	// stamp older than this. The window has to outlast a whole suite run, since a run that overruns it
	// would have its own template swept from under it by a run starting later.
	internal static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

	internal const string MarkerPrefix = "semiplot-template ";

	// starts_with rather than LIKE: the prefix ends in an underscore, which LIKE reads as a
	// single-character wildcard. The name comes back through quote_ident because DROP DATABASE takes no
	// parameter and anything holding CREATEDB can plant a name embedding a double quote. The stamp is
	// read out rather than compared here: a comment any principal can write is not a value to cast
	// inside the predicate.
	private const string StaleTemplatesCommand =
		"""
		SELECT quote_ident(d.datname),
		       shobj_description(d.oid, 'pg_database'),
		       extract(epoch from now())::bigint
		FROM pg_database d
		WHERE starts_with(d.datname, @prefix)
		  AND d.datname <> @current
		  AND NOT EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.datname = d.datname);
		""";

	private const string ServerEpochCommand = "SELECT extract(epoch from now())::bigint;";

	// A discriminator over the seeder assembly, the schema script and the Slice options, so a persistent
	// server cannot serve last week's seed to this week's code.
	public static string Name { get; } = ComputeName();

	public static async Task<Result<string>> BuildAsync(
		PostgresServer postgresServer,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await DropStaleAsync(postgresServer, cancellationToken);

			var provisioned = await SemibaseProvisioner.CreateAsync(postgresServer, Name, cancellationToken);

			if (provisioned.IsFailed)
			{
				return Result.Fail<string>($"semibase {SemibaseProvisioner.CreateCommand} failed: "
					+ string.Join("; ", provisioned.Errors.Select(error => error.Message)));
			}

			await StampAsync(postgresServer, cancellationToken);

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

	// Says that this run has the template in hand, which is what keeps a concurrent run's sweep off it.
	// The stamp goes on before seeding, so a template is marked from the moment it exists; a template
	// caught between creation and its stamp carries none and is swept by nobody.
	private static async Task StampAsync(PostgresServer postgresServer, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(postgresServer.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var read = new NpgsqlCommand(ServerEpochCommand, connection);

		var stamped = (long)(await read.ExecuteScalarAsync(cancellationToken))!;

		// COMMENT ON takes a parameter for neither the database nor the comment. Both values are this
		// run's own — a constant prefix, a name that is the prefix plus a hex digest, and a number read
		// from the server — so nothing a foreign principal chose reaches the statement text.
		await using var stamp = new NpgsqlCommand(
			$"""COMMENT ON DATABASE "{Name}" IS '{MarkerPrefix}{stamped}';""",
			connection);

		await stamp.ExecuteNonQueryAsync(cancellationToken);
	}

	internal static async Task DropStaleAsync(PostgresServer postgresServer, CancellationToken cancellationToken)
	{
		var staleIdentifiers = new List<string>();

		await using (var connection = new NpgsqlConnection(postgresServer.AdminConnectionString))
		{
			await connection.OpenAsync(cancellationToken);

			await using var command = new NpgsqlCommand(StaleTemplatesCommand, connection);

			command.Parameters.AddWithValue("prefix", NamePrefix);
			command.Parameters.AddWithValue("current", Name);

			await using var reader = await command.ExecuteReaderAsync(cancellationToken);

			while (await reader.ReadAsync(cancellationToken))
			{
				var marker = reader.IsDBNull(1) ? null : reader.GetString(1);

				if (IsStale(marker, reader.GetInt64(2)))
				{
					staleIdentifiers.Add(reader.GetString(0));
				}
			}
		}

		foreach (var identifier in staleIdentifiers)
		{
			await ArchiveDatabase.ExecuteAsync(
				postgresServer.AdminConnectionString,
				$"DROP DATABASE IF EXISTS {identifier} WITH (FORCE);",
				cancellationToken);
		}
	}

	// An unreadable stamp is not an old one. The sweep destroys, so anything it cannot date — a database
	// a foreign principal named into the prefix, or one left by a build that stamped nothing — is left
	// where it is rather than guessed about.
	internal static bool IsStale(string? marker, long serverEpochSeconds)
	{
		if (marker is null || !marker.StartsWith(MarkerPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		return long.TryParse(
				marker[MarkerPrefix.Length..],
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var stamped)
			&& serverEpochSeconds - stamped > (long)StaleAfter.TotalSeconds;
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
