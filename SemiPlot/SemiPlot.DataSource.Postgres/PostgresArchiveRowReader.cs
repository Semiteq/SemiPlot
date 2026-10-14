using System.Globalization;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// No bound or timestamp crosses <see cref="ArchiveTimeConverter"/>
/// (docs/architecture/data-integration.md#time-boundary).
/// </summary>
public sealed class PostgresArchiveRowReader : IArchiveRowReader
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly ILogger<PostgresArchiveRowReader> _logger;

	internal PostgresArchiveRowReader(
		NpgsqlDataSource dataSource,
		ArchiveExceptionMapper exceptionMapper,
		ILogger<PostgresArchiveRowReader> logger)
	{
		_dataSource = dataSource;
		_exceptionMapper = exceptionMapper;
		_logger = logger;
	}

	public async Task<Result<long>> CountRowsAsync(
		IReadOnlyList<int> penIds,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.ExportRowCount);

			BindPensRange(command, [.. penIds], from, to);

			var total = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

			return Result.Ok(Convert.ToInt64(total, CultureInfo.InvariantCulture));
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Result.Fail<long>(Map(exception));
		}
	}

	public async Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSeedRowsAsync(
		IReadOnlyList<int> penIds,
		DateTime from,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

			var rows = new List<StoredArchiveRow>();

			foreach (var penId in penIds)
			{
				await using var seed = new NpgsqlCommand(ArchiveStatements.ExportSeedRow, connection);

				BindSeed(seed, penId, from);

				await using var reader = await seed.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

				while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
				{
					rows.Add(new StoredArchiveRow(penId, reader.GetDateTime(0), ReadValue(reader, 1)));
				}
			}

			return Result.Ok<IReadOnlyList<StoredArchiveRow>>([.. rows.OrderBy(row => row.Time)]);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Result.Fail<IReadOnlyList<StoredArchiveRow>>(Map(exception));
		}
	}

	public async Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSliceAsync(
		IReadOnlyList<int> penIds,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await using var command = _dataSource.CreateCommand(ArchiveStatements.ExportSliceRows);

			BindPensRange(command, [.. penIds], from, to);

			var rows = new List<StoredArchiveRow>();

			await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				rows.Add(new StoredArchiveRow(reader.GetInt32(0), reader.GetDateTime(1), ReadValue(reader, 2)));
			}

			return Result.Ok<IReadOnlyList<StoredArchiveRow>>(rows);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Result.Fail<IReadOnlyList<StoredArchiveRow>>(Map(exception));
		}
	}

	internal static void BindSeed(NpgsqlCommand command, int penId, DateTime from)
	{
		command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = penId });
		command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Timestamp) { Value = from });
	}

	internal static void BindPensRange(NpgsqlCommand command, int[] penIds, DateTime from, DateTime to)
	{
		command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
		{
			Value = penIds
		});
		command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Timestamp) { Value = from });
		command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Timestamp) { Value = to });
	}

	private static double? ReadValue(NpgsqlDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
	}

	private Error Map(Exception exception)
	{
		return ArchiveFailureLog.LogIfUnexpected(
			_exceptionMapper.Map(exception, ArchiveStatements.TrendsRelation),
			exception,
			_logger);
	}
}
