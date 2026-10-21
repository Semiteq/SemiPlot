using Npgsql;

using NpgsqlTypes;

using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Integration;

/// <summary>The archive day and the append path a test class writes its own rows with.</summary>
internal static class RealtimeArchive
{
	private const string AppendCommand = """
		INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, 0, @t, @v, @q);
		""";

	// One calendar day, so the write creates the single partition tp2026m01d01 and every appended row falls
	// inside it. Winter under Europe/Berlin, so the conversion out is an unambiguous +1 h.
	public static readonly DateTime Day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	public static readonly DateTime NextDay = Day.AddDays(1);

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
