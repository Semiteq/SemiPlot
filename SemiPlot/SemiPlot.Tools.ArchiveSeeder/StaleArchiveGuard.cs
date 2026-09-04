using System.Globalization;

using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

// Bounds the first tick of Program.FollowAsync: an archive further behind the clock than MaximumAge is refused.
// docs/architecture/bench.md#the-demo-writer
public static class StaleArchiveGuard
{
	public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(5);

	private const string NewestCommand = "SELECT max(t) FROM public.trends;";

	/// <summary>
	/// The archive's newest row, or null when there is no row to start from. An archive further behind
	/// <c>now</c> than <see cref="MaximumAge"/> is refused with a <see cref="SeederException"/>.
	/// </summary>
	public static async Task<DateTime?> CheckAsync(
		string connectionString,
		DateTime now,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(NewestCommand, connection);

		// An empty archive is accepted: nothing a hole could be torn in.
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

	private static string Describe(DateTime newest, TimeSpan age)
	{
		var behind = age.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture);

		return $"public.trends ends at {newest:O}, {behind} minutes behind this machine's clock, and a "
			   + "follow run continues from that edge: the first tick would write the whole span at once, "
			   + "and a day partition for each day it covers. Refill the archive up to now with "
			   + "`converge`, or point --connection at an archive a writer is already keeping live.";
	}
}
