using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Writes the pen catalogue over the provider's <see cref="NpgsqlDataSource"/> and role. Every update, rename
/// and delete must touch exactly one row; none touched is <see cref="ArchiveFault.RowGone"/>.
/// </summary>
public sealed class PostgresPenCatalogueEditor : IPenCatalogueEditor
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly ILogger<PostgresPenCatalogueEditor> _logger;

	internal PostgresPenCatalogueEditor(
		NpgsqlDataSource dataSource,
		ArchiveExceptionMapper exceptionMapper,
		ILogger<PostgresPenCatalogueEditor> logger)
	{
		_dataSource = dataSource;
		_exceptionMapper = exceptionMapper;
		_logger = logger;
	}

	public async Task<Result<PenCatalogue>> ReadAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

			var pens = await ReadStoredPensAsync(connection).ConfigureAwait(false);
			var groups = await ReadStoredGroupsAsync(connection).ConfigureAwait(false);

			return Result.Ok(new PenCatalogue(pens, groups));
		}
		catch (Exception exception)
		{
			return Result.Fail<PenCatalogue>(Map(exception, ArchiveStatements.PenCatalogRelations));
		}
	}

	/// <summary>The number of pens SemiBase's registration function added.</summary>
	public async Task<Result<int>> RegisterNewPensAsync()
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.RegisterNewPens);

			return Result.Ok(await ReadInt32Async(command).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			return Result.Fail<int>(Map(exception, ArchiveStatements.RegisterNewPensFunction));
		}
	}

	public Task<Result> ChangeAsync(StoredPen pen, PenSettingChange change)
	{
		var (statement, parameters) = ChangeStatementOf(change);

		return UpdateOneRowAsync(statement, [.. parameters, IdParameter(pen.Id)], pen.Name, pen.Name);
	}

	/// <summary>The id the server gave the new group.</summary>
	public async Task<Result<int>> CreateGroupAsync(string name)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.CreateGroup);

			command.Parameters.Add(new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = name });

			return Result.Ok(await ReadInt32Async(command).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			return Result.Fail<int>(MapWrite(exception, name));
		}
	}

	public Task<Result> RenameGroupAsync(StoredGroup group, string name)
	{
		return UpdateOneRowAsync(
			ArchiveStatements.RenameGroup,
			[IdParameter(group.Id), new NpgsqlParameter("name", NpgsqlDbType.Text) { Value = name }],
			name,
			group.Name);
	}

	public Task<Result> DeleteGroupAsync(StoredGroup group)
	{
		return UpdateOneRowAsync(ArchiveStatements.DeleteGroup, [IdParameter(group.Id)], group.Name, group.Name);
	}

	/// <summary>Idempotent: adding a membership that exists or removing one that does not succeeds.</summary>
	public async Task<Result> SetMembershipAsync(StoredPen pen, StoredGroup group, bool isMember)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(
				isMember ? ArchiveStatements.AddMembership : ArchiveStatements.RemoveMembership);

			command.Parameters.Add(new NpgsqlParameter("pen_id", NpgsqlDbType.Integer) { Value = pen.Id });
			command.Parameters.Add(new NpgsqlParameter("group_id", NpgsqlDbType.Integer) { Value = group.Id });

			await command.ExecuteNonQueryAsync().ConfigureAwait(false);

			return Result.Ok();
		}
		catch (Exception exception)
		{
			return Result.Fail(MapWrite(exception, SubjectOf(exception, pen, group)));
		}
	}

	// A missing pen and a missing group both raise 23503; the violated key says which one is gone.
	private static string SubjectOf(Exception exception, StoredPen pen, StoredGroup group)
	{
		return exception is PostgresException { ConstraintName: ArchiveStatements.MembershipPenKey }
			? pen.Name
			: group.Name;
	}

	private static async Task<int> ReadInt32Async(NpgsqlCommand command)
	{
		await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

		if (!await reader.ReadAsync().ConfigureAwait(false))
		{
			throw new InvalidOperationException("The statement returned no row.");
		}

		return reader.GetInt32(0);
	}

	private static object NullIfAbsent(object? value)
	{
		return value ?? DBNull.Value;
	}

	private static NpgsqlParameter IdParameter(int id)
	{
		return new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id };
	}

	private static (string Statement, NpgsqlParameter[] Parameters) ChangeStatementOf(PenSettingChange change)
	{
		return change switch
		{
			PenSettingChange.Name name => Value(ArchiveStatements.UpdatePenName, NpgsqlDbType.Text, name.Value),
			PenSettingChange.Unit unit
				=> Value(ArchiveStatements.UpdatePenUnit, NpgsqlDbType.Text, NullIfAbsent(unit.Value)),
			PenSettingChange.Format format
				=> Value(ArchiveStatements.UpdatePenFormat, NpgsqlDbType.Text, NullIfAbsent(format.Value)),
			PenSettingChange.Color color => Value(ArchiveStatements.UpdatePenColor, NpgsqlDbType.Text, color.Value),
			PenSettingChange.LineStyle lineStyle
				=> Value(ArchiveStatements.UpdatePenLineStyle, NpgsqlDbType.Smallint, (short)lineStyle.Value),
			PenSettingChange.EnabledOnStart enabledOnStart
				=> Value(ArchiveStatements.UpdatePenEnabledOnStart, NpgsqlDbType.Boolean, enabledOnStart.Value),
			PenSettingChange.ScaleOnStart scale
				=> (ArchiveStatements.UpdatePenScaleOnStart, ScaleOnStartParameters(scale)),
			PenSettingChange.LogScaleOnStart logScaleOnStart
				=> Value(ArchiveStatements.UpdatePenLogScaleOnStart, NpgsqlDbType.Boolean, logScaleOnStart.Value),
			_ => throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown pen setting change.")
		};
	}

	private static (string Statement, NpgsqlParameter[] Parameters) Value(
		string statement,
		NpgsqlDbType type,
		object value)
	{
		return (statement, [new NpgsqlParameter("value", type) { Value = value }]);
	}

	private static NpgsqlParameter[] ScaleOnStartParameters(PenSettingChange.ScaleOnStart scale)
	{
		return
		[
			new NpgsqlParameter("min", NpgsqlDbType.Double) { Value = NullIfAbsent(scale.Min) },
			new NpgsqlParameter("max", NpgsqlDbType.Double) { Value = NullIfAbsent(scale.Max) }
		];
	}

	private async Task<Result> UpdateOneRowAsync(
		string statement,
		NpgsqlParameter[] parameters,
		string refusedSubject,
		string goneSubject)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(statement);

			command.Parameters.AddRange(parameters);

			var touched = await command.ExecuteNonQueryAsync().ConfigureAwait(false);

			return touched == 1 ? Result.Ok() : Result.Fail(_exceptionMapper.RowGone(goneSubject));
		}
		catch (Exception exception)
		{
			return Result.Fail(MapWrite(exception, refusedSubject));
		}
	}

	private async Task<IReadOnlyList<StoredPen>> ReadStoredPensAsync(NpgsqlConnection connection)
	{
		await using var command = new NpgsqlCommand(ArchiveStatements.StoredPens, connection);
		await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

		var pens = new List<StoredPen>();

		while (await reader.ReadAsync().ConfigureAwait(false))
		{
			pens.Add(ReadStoredPen(reader));
		}

		return pens;
	}

	private static async Task<IReadOnlyList<StoredGroup>> ReadStoredGroupsAsync(NpgsqlConnection connection)
	{
		await using var command = new NpgsqlCommand(ArchiveStatements.StoredGroups, connection);
		await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

		var groups = new List<StoredGroup>();

		while (await reader.ReadAsync().ConfigureAwait(false))
		{
			groups.Add(new StoredGroup(
				reader.GetInt32(StoredGroupColumn.Id),
				reader.GetString(StoredGroupColumn.Name),
				reader.GetFieldValue<int[]>(StoredGroupColumn.Members)));
		}

		return groups;
	}

	private StoredPen ReadStoredPen(NpgsqlDataReader reader)
	{
		var penId = reader.GetInt32(PenCatalogColumn.Id);
		var storedLineStyle = reader.GetInt16(PenCatalogColumn.LineStyle);

		return new StoredPen(
			penId,
			reader.GetString(PenCatalogColumn.Name),
			ReadText(reader, PenCatalogColumn.Unit),
			ReadText(reader, PenCatalogColumn.Format),
			ReadText(reader, PenCatalogColumn.Color),
			StoredLineStyle.Read(storedLineStyle, penId, _logger, () => LogLevel.Warning),
			reader.GetBoolean(PenCatalogColumn.EnabledOnStart),
			ReadBound(reader, PenCatalogColumn.ScaleMinOnStart),
			ReadBound(reader, PenCatalogColumn.ScaleMaxOnStart),
			reader.GetBoolean(PenCatalogColumn.LogScaleOnStart));
	}

	private static string? ReadText(NpgsqlDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
	}

	private static double? ReadBound(NpgsqlDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
	}

	private Error Map(Exception exception, string relation)
	{
		return ArchiveFailureLog.LogIfUnexpected(_exceptionMapper.Map(exception, relation), exception, _logger);
	}

	private Error MapWrite(Exception exception, string subject)
	{
		return ArchiveFailureLog.LogIfUnexpected(_exceptionMapper.MapWrite(exception, subject), exception, _logger);
	}
}
