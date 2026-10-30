using System.Data;

using Npgsql;

using SemiPlot.Core.Data;

namespace SemiPlot.DataSource.Postgres;

/// <summary>Reads the catalogue's statement pair into the raw rows both projections come from.</summary>
internal static class CatalogueRowReader
{
	// docs/architecture/data-integration.md#configuration
	internal const int CatalogueCommandTimeoutSeconds = 5;

	/// <summary>
	/// Reads the statement pair in one repeatable-read transaction, so the pens and the groups come from one
	/// snapshot; a bounded read fails each statement at the catalogue bound.
	/// </summary>
	public static async Task<(IReadOnlyList<RawPenRow> Pens, IReadOnlyList<StoredGroup> Groups)> ReadAsync(
		NpgsqlConnection connection,
		bool isBounded)
	{
		await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead)
			.ConfigureAwait(false);

		var pens = await ReadPensAsync(CreateCommand(ArchiveStatements.StoredPens, connection, isBounded))
			.ConfigureAwait(false);
		var groups = await ReadGroupsAsync(CreateCommand(ArchiveStatements.StoredGroups, connection, isBounded))
			.ConfigureAwait(false);

		await transaction.CommitAsync().ConfigureAwait(false);

		return (pens, groups);
	}

	/// <summary>A catalogue command; a bounded one carries the pair's own statement bound.</summary>
	internal static NpgsqlCommand CreateCommand(string statement, NpgsqlConnection connection, bool isBounded)
	{
		var command = new NpgsqlCommand(statement, connection);

		if (isBounded)
		{
			command.CommandTimeout = CatalogueCommandTimeoutSeconds;
		}

		return command;
	}

	private static async Task<IReadOnlyList<RawPenRow>> ReadPensAsync(NpgsqlCommand command)
	{
		await using (command)
		{
			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			var rows = new List<RawPenRow>();

			while (await reader.ReadAsync().ConfigureAwait(false))
			{
				rows.Add(new RawPenRow(
					reader.GetInt32(StoredPenColumn.Id),
					reader.GetString(StoredPenColumn.Name),
					ReadText(reader, StoredPenColumn.Unit),
					ReadText(reader, StoredPenColumn.Format),
					ReadText(reader, StoredPenColumn.Color),
					reader.GetInt16(StoredPenColumn.LineStyle),
					reader.GetBoolean(StoredPenColumn.EnabledOnStart),
					ReadBound(reader, StoredPenColumn.ScaleMinOnStart),
					ReadBound(reader, StoredPenColumn.ScaleMaxOnStart),
					reader.GetBoolean(StoredPenColumn.LogScaleOnStart)));
			}

			return rows;
		}
	}

	private static async Task<IReadOnlyList<StoredGroup>> ReadGroupsAsync(NpgsqlCommand command)
	{
		await using (command)
		{
			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			var groups = new List<StoredGroup>();

			while (await reader.ReadAsync().ConfigureAwait(false))
			{
				groups.Add(new StoredGroup(
					reader.GetInt32(StoredGroupColumn.Id),
					reader.GetString(StoredGroupColumn.Name),
					reader.GetFieldValue<int[]>(StoredGroupColumn.Members),
					reader.GetBoolean(StoredGroupColumn.FoldedOnStart)));
			}

			return groups;
		}
	}

	private static string? ReadText(NpgsqlDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	}

	private static double? ReadBound(NpgsqlDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
	}
}
