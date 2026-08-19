using Microsoft.Extensions.Logging;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Answers which relation a <c>42P01</c> refers to. PostgreSQL leaves the structured table-name field
/// empty for <c>undefined_table</c> — it fills that field for constraint violations, not on this path —
/// so the name survives only in the message text, which this project does not route on; and the extent
/// statement touches two relations, so neither reading the message nor assuming an order can tell them
/// apart. A <c>to_regclass</c> lookup over both can.
/// <para>
/// It sits in the read path rather than inside <see cref="ArchiveExceptionMapper"/>: it is a network
/// round trip, and putting it in the mapper would make the mapper asynchronous, give it a data-source
/// dependency and end the unit-testability that keeps it honest.
/// </para>
/// </summary>
internal sealed class MissingRelationProbe(ArchiveDataSource dataSource, ILogger<MissingRelationProbe> logger)
{
	// Command Timeout=0 would otherwise let the error path hang without bound, and an error path that
	// hangs is worse than one that answers nothing.
	private const int ProbeCommandTimeoutSeconds = 10;

	/// <summary>
	/// The name of the relation that is absent, or null when both resolve or the probe could not run. The
	/// caller reports its own statement's fallback relation for a null, because the caller knows which
	/// relations its statement touches and this type does not.
	/// </summary>
	public async Task<string?> FindMissingRelationAsync()
	{
		try
		{
			// A fresh connection, never the failed command's: that one may sit in an aborted transaction,
			// where every further statement answers 25P02. CancellationToken.None throughout, because a
			// caller's token is frequently already cancelled by the time its read fails, which would leave
			// the probe unable to run at all.
			await using var connection = await dataSource
				.OpenConnectionAsync(CancellationToken.None)
				.ConfigureAwait(false);

			await using var command = connection.CreateCommand();

			command.CommandText = ArchiveStatements.RelationProbe;
			command.CommandTimeout = ProbeCommandTimeoutSeconds;

			await using var reader = await command
				.ExecuteReaderAsync(CancellationToken.None)
				.ConfigureAwait(false);

			if (!await reader.ReadAsync(CancellationToken.None).ConfigureAwait(false))
			{
				return null;
			}

			return Resolve(reader.GetBoolean(0), reader.GetBoolean(1));
		}
		catch (Exception exception)
		{
			// Never propagated: the mapper is called without a name and still produces a usable error.
			// Re-entering the mapper for the probe's own failure would be unbounded recursion.
			logger.LogWarning(
				exception,
				"The missing-relation probe could not run, so the read reports its own fallback relation.");

			return null;
		}
	}

	// Neither present answers semiplot_tags rather than nothing: provisioning precedes commissioning, so
	// the remedy is `semibase create`, and naming trends there would send the operator to start a SCADA
	// against a database that does not carry SemiBase's own object yet.
	internal static string? Resolve(bool tagCatalogPresent, bool trendsPresent)
	{
		return (tagCatalogPresent, trendsPresent) switch
		{
			(false, true) => ArchiveStatements.TagCatalogRelation,
			(true, false) => ArchiveStatements.TrendsRelation,
			(false, false) => ArchiveStatements.TagCatalogRelation,
			_ => null
		};
	}
}
