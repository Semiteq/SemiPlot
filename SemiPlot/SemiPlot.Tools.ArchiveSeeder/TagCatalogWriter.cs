using FluentResults;

using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

// The write goes through the admin connection because scada_writer holds no privilege on
// semiplot_tags — on a site that table is filled by hand during commissioning
// (docs/architecture/postgres-instance.md).
public sealed class TagCatalogWriter(string adminConnectionString)
{
	private const string UpsertCommand =
		"""
		INSERT INTO public.semiplot_tags (id, name, group_name, color, line_style)
		VALUES (@id, @name, @group_name, @color, @line_style)
		ON CONFLICT (id) DO UPDATE
		SET name = EXCLUDED.name,
			group_name = EXCLUDED.group_name,
			color = EXCLUDED.color,
			line_style = EXCLUDED.line_style;
		""";

	public async Task<Result<int>> WriteAsync(
		IEnumerable<SyntheticPen> pens,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await using var connection = new NpgsqlConnection(adminConnectionString);

			await connection.OpenAsync(cancellationToken);

			var written = 0;

			foreach (var pen in pens)
			{
				await UpsertAsync(connection, pen, cancellationToken);

				written++;
			}

			return Result.Ok(written);
		}
		catch (Exception exception) when (ArchiveWriter.IsReportable(exception))
		{
			return Result.Fail<int>(new ExceptionalError(exception.Message, exception));
		}
	}

	private static async Task UpsertAsync(
		NpgsqlConnection connection,
		SyntheticPen pen,
		CancellationToken cancellationToken)
	{
		await using var command = new NpgsqlCommand(UpsertCommand, connection);

		// The archive's own key is an integer, and semiplot_tags.id matches trends.id.
		command.Parameters.AddWithValue("id", (int)pen.PenId);
		command.Parameters.AddWithValue("name", pen.Name);
		command.Parameters.AddWithValue("group_name", pen.Group);
		command.Parameters.AddWithValue("color", pen.Color);
		command.Parameters.AddWithValue("line_style", (short)pen.LineStyle);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}
