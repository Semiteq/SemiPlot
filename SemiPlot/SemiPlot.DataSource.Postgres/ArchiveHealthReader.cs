using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The archive's operational health, read once at startup on a connection of its own: states that are a
/// fault for the operator to act on and stop nothing for SemiPlot. It answers with the warnings it found,
/// zero or more, and never with a failure.
/// <para>
/// One state today — a non-empty default partition, which
/// docs/architecture/scada-archive.md names as a fault signal. It is a warning rather than a startup
/// failure because every read still returns those rows: only partition elimination is lost, so refusing to
/// start would hide a readable archive from its operator over a planning fault written on the SCADA side.
/// </para>
/// <para>
/// It is a cold-path reader in the shape <see cref="StatementTimeoutReader"/> already uses — a fresh
/// connection, its own short bound, every failure swallowed into a log line. A health check that cannot run
/// reports nothing: a degraded probe must not become a second failure plane beside the reads that matter,
/// and "the archive might be unhealthy, we could not tell" is not a state an operator can act on.
/// </para>
/// </summary>
public sealed class ArchiveHealthReader
{
	// This runs on the startup path, where the operator is looking at nothing yet, so it carries a bound
	// well below the probe's own read bound rather than the data source's five-minute backstop: a health
	// answer that arrives after the window would have opened is worth less than the wait it cost.
	private const int ReadCommandTimeoutSeconds = 10;

	private readonly ArchiveDataSource _dataSource;

	private readonly PostgresConnectionSettings _settings;

	private readonly ILogger<ArchiveHealthReader> _logger;

	public ArchiveHealthReader(
		ArchiveDataSource dataSource,
		PostgresConnectionSettings settings,
		ILogger<ArchiveHealthReader> logger)
	{
		ArgumentNullException.ThrowIfNull(dataSource);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(logger);

		_dataSource = dataSource;
		_settings = settings;
		_logger = logger;
	}

	/// <summary>
	/// Every health warning the archive answers with. Empty means either a healthy archive or a check that
	/// could not run; the log line separates the two, and neither is something the caller branches on.
	/// </summary>
	public async Task<IReadOnlyList<IError>> ReadAsync(CancellationToken cancellationToken = default)
	{
		var occupied = await ReadDefaultPartitionOccupancyAsync(cancellationToken).ConfigureAwait(false);

		if (occupied != true)
		{
			return [];
		}

		var warning = new ArchiveDefaultPartitionNotEmptyError(
			_settings.Host,
			_settings.Port,
			_settings.Database,
			ArchiveStatements.DefaultPartitionRelation);

		_logger.LogWarning("{Warning}", warning.Message);

		return [warning];
	}

	// Null when the check could not run, cancellation included — a check the caller stopped is a check that
	// did not run, and nothing downstream branches on which of the two it was. Never mapped through
	// ArchiveExceptionMapper: this read failing is not a state the operator is offered a remedy for, and
	// turning it into the vocabulary would put a second, weaker source behind types the reads that matter
	// already raise.
	private async Task<bool?> ReadDefaultPartitionOccupancyAsync(CancellationToken cancellationToken)
	{
		try
		{
			await using var connection = await _dataSource
				.OpenConnectionAsync(cancellationToken)
				.ConfigureAwait(false);

			await using var command = connection.CreateCommand();

			command.CommandText = ArchiveStatements.DefaultPartitionOccupancy;
			command.CommandTimeout = ReadCommandTimeoutSeconds;

			return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
		}
		catch (Exception exception)
		{
			_logger.LogWarning(
				exception,
				"The archive health check could not run, so this start reports no health warning. The "
				+ "reads the chart depends on are unaffected.");

			return null;
		}
	}
}
