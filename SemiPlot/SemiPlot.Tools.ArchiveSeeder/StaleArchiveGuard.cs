using System.Globalization;

using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

// What bounds the first tick of Program.FollowAsync. The loop starts just past max(t), so the appended
// rows continue the fill instead of standing apart from it behind a hole nothing in the archive marks;
// the price of that start is the span of the first tick, which against an archive filled weeks ago would
// be those weeks of rows and a day partition for each. Refusing an archive further behind the clock
// than MaximumAge is what holds that span inside the bound.
public static class StaleArchiveGuard
{
	// The same bound scripts/bench-demo.ps1 uses to decide that an archive is live and needs no refill.
	// Its floor is the tick cadence: a running writer keeps max(t) within a second or two of the clock, so
	// five minutes refuses no live archive. Its ceiling is the first tick, which writes whatever an accepted
	// archive is behind the clock.
	public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);

	// to_regclass keeps a database provisioning never touched out of this read: it answers NULL exactly as
	// an empty archive does, and the missing table is ArchiveWriter's message to give, not this one's.
	private const string NewestCommand = """
	                                     SELECT CASE
	                                     	WHEN to_regclass('public.trends') IS NULL THEN NULL
	                                     	ELSE (SELECT max(t) FROM public.trends)
	                                     END;
	                                     """;

	/// <summary>
	/// The archive's newest row, or null when there is no row to start from. An archive further behind
	/// <paramref name="now"/> than <see cref="MaximumAge"/> is refused with a <see cref="SeederException"/>.
	/// </summary>
	public static async Task<DateTime?> CheckAsync(
		string connectionString,
		DateTime now,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(NewestCommand, connection);

		// An archive with no rows carries nothing a hole could be torn in, so it is accepted rather than
		// refused, and DBNull is what it and an unprovisioned database both answer.
		if (await command.ExecuteScalarAsync(cancellationToken) is not DateTime newest)
		{
			return null;
		}

		var age = now - newest;

		if (age > MaximumAge)
		{
			throw new SeederException(Describe(newest, age));
		}

		return newest;
	}

	// Where the follow loop starts: one millisecond past the archive's newest row, never on it. The follow
	// lattice is absolute, so an archive whose newest row a previous follow run wrote carries that row
	// exactly on a lattice point, and LiveTailGenerator's span start is inclusive — a loop resuming on the
	// edge regenerates that row into a COPY that has no conflict handling. A millisecond is the smallest
	// step that separates two rows, since ArchiveRow truncates every timestamp to one and the column is
	// timestamp(3). An archive with no rows has no edge to continue, and the clock is the start.
	public static DateTime StartFrom(DateTime? newestRow, DateTime clock)
	{
		return newestRow is { } newest ? newest.AddMilliseconds(1.0) : clock;
	}

	private static string Describe(DateTime newest, TimeSpan age)
	{
		var behind = age.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture);

		return $"public.trends ends at {newest:O}, {behind} minutes behind this machine's clock, and a "
			   + "follow run continues from that edge: the first tick would write the whole span at once, "
			   + "and a day partition for each day it covers. Refill the archive up to now with "
			   + "`pwsh scripts/bench-demo.ps1`, or point --connection at an archive a writer is already "
			   + "keeping live.";
	}
}
