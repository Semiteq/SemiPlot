using System.Globalization;

using FluentResults;

using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

// What bounds the first tick of Program.FollowAsync. The loop starts just past max(t), so the appended
// rows continue the fill instead of standing apart from it behind a hole nothing in the archive marks;
// the price of that start is the span of the first tick, which against an archive filled weeks ago would
// be those weeks of rows and a day partition for each. Refusing an archive further behind the clock
// than MaximumAge is what holds that span inside the bound, and reading max(t) once, before anything
// is written, is what both the refusal and the start need.
//
// scripts/bench-demo.ps1 keeps the harness clear of this by filling up to the wall clock, so what the
// refusal covers is the path the harness cannot reach: a --connection pointed by hand at an archive of
// somebody's own, which docs/architecture/bench.md invites.
public static class StaleArchiveGuard
{
	// The same bound scripts/bench-demo.ps1 uses to decide that an archive is live and needs no refill,
	// and it answers the same question from the other side.
	//
	// Its floor is the tick cadence: a running writer keeps max(t) within a second or two of the clock at
	// the demo's --follow 1, so five minutes is three hundred ticks of margin and no live archive is ever
	// refused. Its ceiling is the first tick: the loop starts at max(t), so whatever an accepted archive
	// is behind the clock is what that tick writes, and five minutes of rows across at most one day
	// boundary is what the bound admits.
	public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);

	// to_regclass keeps a database provisioning never touched out of this read: it answers NULL exactly as
	// an empty archive does, and the missing table is ArchiveWriter's message to give, not this one's.
	private const string NewestCommand = """
	                                     SELECT CASE
	                                     	WHEN to_regclass('public.trends') IS NULL THEN NULL
	                                     	ELSE (SELECT max(t) FROM public.trends)
	                                     END;
	                                     """;

	// The accepted archive's newest row rides back on the Result, so the caller starting there needs no
	// second read of max(t). It is null exactly when there is no row to start from.
	public static async Task<Result<DateTime?>> CheckAsync(
		string connectionString,
		DateTime now,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await using var connection = new NpgsqlConnection(connectionString);

			await connection.OpenAsync(cancellationToken);

			await using var command = new NpgsqlCommand(NewestCommand, connection);

			// An archive with no rows carries nothing a hole could be torn in, so it is accepted rather
			// than refused, and DBNull is what it and an unprovisioned database both answer.
			if (await command.ExecuteScalarAsync(cancellationToken) is not DateTime newest)
			{
				return Result.Ok<DateTime?>(null);
			}

			var age = now - newest;

			return age <= MaximumAge
				? Result.Ok<DateTime?>(newest)
				: Result.Fail<DateTime?>(Describe(newest, age));
		}
		catch (Exception exception) when (ArchiveWriter.IsReportable(exception))
		{
			return Result.Fail<DateTime?>(new ExceptionalError(exception.Message, exception));
		}
	}

	// Where the follow loop starts: one millisecond past the archive's newest row, never on it. The follow
	// lattice is absolute, so an archive whose newest row a previous follow run wrote carries that row
	// exactly on a lattice point, and LiveTailGenerator's span start is inclusive — a loop resuming on the
	// edge regenerates that row into a COPY that has no conflict handling, and the run dies on its first
	// tick with a duplicate key. A millisecond is the smallest step that separates two rows, since
	// ArchiveRow truncates every timestamp to one and the column is timestamp(3), so the first live row
	// still lands within one change interval of the edge and the seam stays closed. An archive with no
	// rows has no edge to continue, and the clock is the start.
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
