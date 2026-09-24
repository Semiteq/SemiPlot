using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

// The write goes through the admin connection because scada_writer holds no privilege on
// semiplot_tags. On a site those tables are filled by hand during commissioning
// (docs/architecture/postgres-instance.md).
public sealed class TagCatalogWriter(string adminConnectionString)
{
	private const string UpsertTagCommand =
		"""
		INSERT INTO public.semiplot_tags
			(id, name, unit, format, color, line_style, enabled_on_start, scale_min, scale_max)
		VALUES (@id, @name, @unit, @format, @color, @line_style, @enabled_on_start, @scale_min, @scale_max)
		ON CONFLICT (id) DO UPDATE
		SET name = EXCLUDED.name,
			unit = EXCLUDED.unit,
			format = EXCLUDED.format,
			color = EXCLUDED.color,
			line_style = EXCLUDED.line_style,
			enabled_on_start = EXCLUDED.enabled_on_start,
			scale_min = EXCLUDED.scale_min,
			scale_max = EXCLUDED.scale_max;
		""";

	private const string InsertGroupsCommand =
		"""
		INSERT INTO public.semiplot_groups (name)
		SELECT DISTINCT unnest(@names)
		ON CONFLICT (name) DO NOTHING;
		""";

	// The group table is only ever inserted into, so a rerun after a group was renamed or dropped would
	// leave the old name behind with no member and the panel would draw an empty header.
	private const string DropEmptyGroupsCommand =
		"""
		DELETE FROM public.semiplot_groups AS grp
		WHERE NOT EXISTS (
			SELECT 1 FROM public.semiplot_pen_groups membership WHERE membership.group_id = grp.id);
		""";

	// The pen's memberships are replaced rather than added to, so a rerun with a changed catalogue
	// leaves no membership the current one does not state.
	private const string ClearMembershipsCommand =
		"DELETE FROM public.semiplot_pen_groups WHERE pen_id = @pen_id;";

	private const string InsertMembershipCommand =
		"""
		INSERT INTO public.semiplot_pen_groups (pen_id, group_id)
		SELECT @pen_id, id FROM public.semiplot_groups WHERE name = @name;
		""";

	/// <summary>
	/// The number of pens written. A connection that cannot be made or a rejected statement throws, and
	/// the whole catalogue is one transaction: a half-written one would draw pens into groups they left.
	/// </summary>
	public async Task<int> WriteAsync(IEnumerable<SyntheticPen> pens, CancellationToken cancellationToken = default)
	{
		var catalogue = pens as IReadOnlyList<SyntheticPen> ?? [.. pens];

		await using var connection = new NpgsqlConnection(adminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

		await InsertGroupsAsync(connection, transaction, catalogue, cancellationToken);

		foreach (var pen in catalogue)
		{
			await UpsertTagAsync(connection, transaction, pen, cancellationToken);
			await WriteMembershipsAsync(connection, transaction, pen, cancellationToken);
		}

		await ExecuteAsync(connection, transaction, DropEmptyGroupsCommand, cancellationToken);
		await transaction.CommitAsync(cancellationToken);

		return catalogue.Count;
	}

	private static async Task InsertGroupsAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		IEnumerable<SyntheticPen> pens,
		CancellationToken cancellationToken)
	{
		string[] names = [.. pens.SelectMany(pen => pen.Groups).Distinct(StringComparer.Ordinal)];

		if (names.Length == 0)
		{
			return;
		}

		await using var command = new NpgsqlCommand(InsertGroupsCommand, connection, transaction);

		command.Parameters.AddWithValue("names", names);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task ExecuteAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		string commandText,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(commandText, connection, transaction);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task UpsertTagAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		SyntheticPen pen,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(UpsertTagCommand, connection, transaction);

		command.Parameters.AddWithValue("id", pen.PenId);
		command.Parameters.AddWithValue("name", pen.Name);
		command.Parameters.AddWithValue("unit", (object?)pen.Unit ?? DBNull.Value);
		command.Parameters.AddWithValue("format", (object?)pen.Format ?? DBNull.Value);
		command.Parameters.AddWithValue("color", pen.Color);
		command.Parameters.AddWithValue("line_style", (short)pen.LineStyle);
		command.Parameters.AddWithValue("enabled_on_start", pen.EnabledOnStart);
		command.Parameters.AddWithValue("scale_min", (object?)pen.ScaleMin ?? DBNull.Value);
		command.Parameters.AddWithValue("scale_max", (object?)pen.ScaleMax ?? DBNull.Value);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	private static async Task WriteMembershipsAsync(
		NpgsqlConnection connection,
		NpgsqlTransaction transaction,
		SyntheticPen pen,
		CancellationToken cancellationToken)
	{
		await using (var clear = new NpgsqlCommand(ClearMembershipsCommand, connection, transaction))
		{
			clear.Parameters.AddWithValue("pen_id", pen.PenId);

			await clear.ExecuteNonQueryAsync(cancellationToken);
		}

		foreach (var group in pen.Groups)
		{
			await using var insertMembership = new NpgsqlCommand(InsertMembershipCommand, connection, transaction);

			insertMembership.Parameters.AddWithValue("pen_id", pen.PenId);
			insertMembership.Parameters.AddWithValue("name", group);

			await insertMembership.ExecuteNonQueryAsync(cancellationToken);
		}
	}
}
