using System.Globalization;

using Microsoft.Extensions.Logging;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads the server's effective <c>statement_timeout</c> when a read has already failed with
/// <c>57014</c>, so that <c>ArchiveQueryTimedOutError.Timeout</c> carries the bound the server actually
/// applied. The value is per-site: <c>statement_timeout</c> is a <c>USERSET</c> GUC, SemiPlot sends none,
/// so the effective bound is the reader role's own and is knowable no other way.
/// <para>
/// It sits in the read path rather than inside <see cref="ArchiveExceptionMapper"/>: it is a network round
/// trip, and putting it in the mapper would make the mapper asynchronous, give it a data-source dependency
/// and end the unit-testability that keeps it honest.
/// </para>
/// </summary>
internal sealed class StatementTimeoutReader(ArchiveDataSource dataSource, ILogger<StatementTimeoutReader> logger)
{
	// The one read that cannot use the per-command backstop, so it carries an explicit short one of its
	// own: this runs on a server that has just proved slow, and an error path that hangs is worse than one
	// that answers nothing. It bounds the command only — Npgsql bounds the connection open separately, at its
	// 15 s default — so a 57014 can be held up by about 25 s in total before the error reaches the caller.
	private const int ReadCommandTimeoutSeconds = 10;

	/// <summary>
	/// The server's effective bound, <see cref="TimeSpan.Zero"/> when the server bounds nothing, or null
	/// when the read could not run. The caller reports no number for either of the last two, because
	/// neither has a bound to report.
	/// </summary>
	public async Task<TimeSpan?> ReadEffectiveBoundAsync()
	{
		try
		{
			// A fresh connection, never the failed command's: that one may sit in an aborted transaction,
			// where every further statement answers 25P02. CancellationToken.None throughout, because a
			// caller's token is frequently already cancelled by the time its read fails, which would leave
			// the reader unable to run at all.
			await using var connection = await dataSource
				.OpenConnectionAsync(CancellationToken.None)
				.ConfigureAwait(false);

			await using var command = connection.CreateCommand();

			command.CommandText = ArchiveStatements.EffectiveStatementTimeout;
			command.CommandTimeout = ReadCommandTimeoutSeconds;

			var setting = await command
				.ExecuteScalarAsync(CancellationToken.None)
				.ConfigureAwait(false) as string;

			var bound = TimeSpan.FromMilliseconds(ParseMilliseconds(setting));

			if (bound == TimeSpan.Zero)
			{
				logger.LogWarning(
					"No bound was taken from statement_timeout, which read {Setting}: either the archive bounds "
					+ "no statement or the answer did not parse. The timed-out read reports no bound.",
					setting);
			}

			return bound;
		}
		catch (Exception exception)
		{
			// Never propagated: the mapper is called without a bound and still produces a usable error.
			// Re-entering the mapper for the reader's own failure would be unbounded recursion.
			logger.LogWarning(
				exception,
				"The statement-timeout read could not run, so the timed-out read reports no bound.");

			return null;
		}
	}

	internal static int ParseMilliseconds(string? setting)
	{
		return int.TryParse(setting, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
			&& milliseconds > 0
				? milliseconds
				: 0;
	}
}
