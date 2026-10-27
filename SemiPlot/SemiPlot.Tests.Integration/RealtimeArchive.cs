using System.Reactive.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Integration;

/// <summary>The archive rows, the subscription and the append path the realtime test classes share.</summary>
internal static class RealtimeArchive
{
	private const string AppendCommand = """
		INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, 0, @t, @v, @q);
		""";

	// A strict subset of the seeded identifiers, so a tick that ignored @ids would fail.
	public static readonly int[] SubscribedPenIds = [1, 2];

	// One calendar day, so the write creates the single partition tp2026m01d01 and every appended row falls
	// inside it. Winter under Europe/Berlin, so the conversion out is an unambiguous +1 h.
	public static readonly DateTime Day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	public static readonly DateTime NextDay = Day.AddDays(1);

	// The newest seeded row, which is what each subscription's baseline answers. Everything appended sits
	// after it and hours short of the partition's upper bound.
	public static readonly DateTime SeededLast = Day.AddMinutes(1);

	public static RealtimePoll NewPoll(IServiceProvider services, ILogger? logger = null)
	{
		return new RealtimePoll(
			services.GetRequiredService<NpgsqlDataSource>(),
			services.GetRequiredService<ArchiveTimeConverter>(),
			services.GetRequiredService<ArchiveExceptionMapper>(),
			SubscribedPenIds,
			logger ?? NullLogger.Instance);
	}

	// An answered poll with no change emits every interval and would open a gate meant for a delivered row.
	public static IObservable<IReadOnlyList<Sample>> SampleBatches(this IObservable<LivePoll> polls)
	{
		return polls
			.Where(poll => poll.Samples.Count > 0)
			.Select(poll => poll.Samples);
	}

	// The rename keeps the rows and the day partitions, so the recovering poll reads the archive the class seeded.
	public static Task HideTrendsAsync(ArchiveDatabase database, CancellationToken cancellationToken)
	{
		return ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			"ALTER TABLE public.trends RENAME TO trends_hidden_by_test;",
			cancellationToken);
	}

	public static Task RestoreTrendsAsync(ArchiveDatabase database, CancellationToken cancellationToken)
	{
		return ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			"ALTER TABLE public.trends_hidden_by_test RENAME TO trends;",
			cancellationToken);
	}

	// Two rows per subscribed variable, the second of them the baseline every subscription starts from.
	public static IReadOnlyList<ArchiveRow> SeededRows()
	{
		var rows = new List<ArchiveRow>();

		foreach (var penId in SubscribedPenIds)
		{
			rows.Add(new ArchiveRow(penId, ArchiveRow.RawLayer, Day, penId, ArchiveRow.OrdinaryQuality));
			rows.Add(new ArchiveRow(penId, ArchiveRow.RawLayer, SeededLast, penId, ArchiveRow.OrdinaryQuality));
		}

		return rows;
	}

	// Written as scada_writer, the role the SCADA itself writes with, and one row at a time: a COPY would go
	// through ArchiveWriter, which refuses an archive already carrying rows. The day partition is created
	// first, because an INSERT into a partitioned table with no matching partition lands in the default one.
	public static async Task AppendAsync(
		ArchiveDatabase database,
		int penId,
		DateTime archiveLocal,
		double? value,
		int quality = ArchiveRow.OrdinaryQuality,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var partition = new NpgsqlCommand(PartitionScript.CreateStatement(archiveLocal.Date), connection);

		await partition.ExecuteNonQueryAsync(cancellationToken);

		await using var command = new NpgsqlCommand(AppendCommand, connection);

		command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = penId });
		command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Timestamp) { Value = archiveLocal });
		command.Parameters.Add(
			new NpgsqlParameter("v", NpgsqlDbType.Double) { Value = (object?)value ?? DBNull.Value });
		command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Integer) { Value = quality });

		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}
