using System.Globalization;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads and writes <c>semiplot_markers</c> over the provider's <see cref="NpgsqlDataSource"/> and role. A move,
/// rename or lock touching no row is <see cref="ArchiveFault.MarkerGone"/>; a delete touching none succeeds.
/// </summary>
public sealed class PostgresMarkerStore : IMarkerStore
{
	internal const string SubjectTimeFormat = "yyyy-MM-dd HH:mm:ss";

	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly ILogger<PostgresMarkerStore> _logger;

	internal PostgresMarkerStore(
		NpgsqlDataSource dataSource,
		ArchiveExceptionMapper exceptionMapper,
		ILogger<PostgresMarkerStore> logger)
	{
		_dataSource = dataSource;
		_exceptionMapper = exceptionMapper;
		_logger = logger;
	}

	public async Task<Result<IReadOnlyList<StoredMarker>>> ReadAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.StoredMarkers);
			await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

			var markers = new List<StoredMarker>();

			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				var kind = (MarkerKind)reader.GetInt16(StoredMarkerColumn.Kind);

				if (!Enum.IsDefined(kind))
				{
					return Result.Fail<IReadOnlyList<StoredMarker>>(_exceptionMapper.ShapeUnexpected(
						FormattableString.Invariant($"semiplot_markers.kind holds {(int)kind}, which MarkerKind does not define.")));
				}

				markers.Add(new StoredMarker(
					reader.GetInt64(StoredMarkerColumn.Id),
					reader.GetFieldValue<DateTime>(StoredMarkerColumn.Time),
					reader.IsDBNull(StoredMarkerColumn.Label) ? null : reader.GetString(StoredMarkerColumn.Label),
					kind,
					reader.GetBoolean(StoredMarkerColumn.Locked)));
			}

			return Result.Ok<IReadOnlyList<StoredMarker>>(markers);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Result.Fail<IReadOnlyList<StoredMarker>>(ArchiveFailureLog.LogIfUnexpected(
				_exceptionMapper.Map(exception, ArchiveStatements.MarkersRelation), exception, _logger));
		}
	}

	/// <summary>The id the server gave the new marker.</summary>
	public async Task<Result<long>> AddAsync(DateTime timeUtc, string? label)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.AddMarker);

			command.Parameters.Add(TimeParameter(timeUtc));
			command.Parameters.Add(LabelParameter(label));

			var id = await command.ExecuteScalarAsync().ConfigureAwait(false)
				?? throw new InvalidOperationException("The statement returned no row.");

			return Result.Ok(Convert.ToInt64(id, CultureInfo.InvariantCulture));
		}
		catch (Exception exception)
		{
			return Result.Fail<long>(MapWrite(exception, SubjectOf(timeUtc, label)));
		}
	}

	public Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc)
	{
		var storedSubject = SubjectOf(marker.TimeUtc, marker.Label);

		return UpdateOneRowAsync(
			ArchiveStatements.MoveMarker,
			[IdParameter(marker.Id), TimeParameter(timeUtc)],
			refusedSubject: storedSubject,
			goneSubject: storedSubject);
	}

	public Task<Result> RenameAsync(StoredMarker marker, string? label)
	{
		return UpdateOneRowAsync(
			ArchiveStatements.RenameMarker,
			[IdParameter(marker.Id), LabelParameter(label)],
			refusedSubject: SubjectOf(marker.TimeUtc, label),
			goneSubject: SubjectOf(marker.TimeUtc, marker.Label));
	}

	public Task<Result> SetLockedAsync(StoredMarker marker, bool isLocked)
	{
		var subject = SubjectOf(marker.TimeUtc, marker.Label);

		return UpdateOneRowAsync(
			ArchiveStatements.SetMarkerLocked,
			[IdParameter(marker.Id), new NpgsqlParameter("locked", NpgsqlDbType.Boolean) { Value = isLocked }],
			refusedSubject: subject,
			goneSubject: subject);
	}

	/// <summary>Idempotent: deleting a marker another window already removed succeeds.</summary>
	public async Task<Result> DeleteAsync(StoredMarker marker)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.DeleteMarker);

			command.Parameters.Add(IdParameter(marker.Id));

			await command.ExecuteNonQueryAsync().ConfigureAwait(false);

			return Result.Ok();
		}
		catch (Exception exception)
		{
			return Result.Fail(MapWrite(exception, SubjectOf(marker.TimeUtc, marker.Label)));
		}
	}

	private static string SubjectOf(DateTime timeUtc, string? label)
	{
		return string.IsNullOrEmpty(label)
			? timeUtc.ToLocalTime().ToString(SubjectTimeFormat, CultureInfo.CurrentCulture)
			: label;
	}

	private static NpgsqlParameter IdParameter(long id)
	{
		return new NpgsqlParameter("id", NpgsqlDbType.Bigint) { Value = id };
	}

	private static NpgsqlParameter TimeParameter(DateTime timeUtc)
	{
		return new NpgsqlParameter("t", NpgsqlDbType.TimestampTz) { Value = timeUtc };
	}

	private static NpgsqlParameter LabelParameter(string? label)
	{
		return new NpgsqlParameter("label", NpgsqlDbType.Text) { Value = (object?)label ?? DBNull.Value };
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

			return touched == 1
				? Result.Ok()
				: Result.Fail(_exceptionMapper.MarkerGone(goneSubject));
		}
		catch (Exception exception)
		{
			return Result.Fail(MapWrite(exception, refusedSubject));
		}
	}

	private Error MapWrite(Exception exception, string subject)
	{
		return ArchiveFailureLog.LogIfUnexpected(
			_exceptionMapper.MapWrite(exception, subject, ArchiveStatements.MarkersRelation), exception, _logger);
	}
}
