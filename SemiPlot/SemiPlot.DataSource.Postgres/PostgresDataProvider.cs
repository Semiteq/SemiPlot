using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads the archive over the pooled <see cref="ArchiveDataSource"/>. <see cref="QueryPensAsync"/> answers
/// the configured variables, <see cref="QueryArchiveExtentAsync"/> the span they cover and
/// <see cref="QueryHistoryAsync"/> a window of one layer, all crossing the boundary in UTC. Every failure
/// leaves through the public error vocabulary — nothing internal crosses
/// the boundary — and a <c>42P01</c> is resolved to a relation name by <see cref="MissingRelationProbe"/>
/// before it is mapped, with the read supplying its own statement's fallback when the probe cannot answer.
/// </summary>
public sealed class PostgresDataProvider : IDataProvider
{
	private readonly ArchiveDataSource _dataSource;
	private readonly ArchiveTimeConverter _timeConverter;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly MissingRelationProbe _missingRelationProbe;
	private readonly ILogger<PostgresDataProvider> _logger;

	// Internal because two of its parameters are: a public constructor over an internal type is CS0051.
	internal PostgresDataProvider(
		ArchiveDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		MissingRelationProbe missingRelationProbe,
		ILogger<PostgresDataProvider> logger)
	{
		ArgumentNullException.ThrowIfNull(dataSource);
		ArgumentNullException.ThrowIfNull(timeConverter);
		ArgumentNullException.ThrowIfNull(exceptionMapper);
		ArgumentNullException.ThrowIfNull(missingRelationProbe);
		ArgumentNullException.ThrowIfNull(logger);

		_dataSource = dataSource;
		_timeConverter = timeConverter;
		_exceptionMapper = exceptionMapper;
		_missingRelationProbe = missingRelationProbe;
		_logger = logger;
	}

	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		return Observable.Empty<IReadOnlyList<Sample>>();
	}

	/// <summary>
	/// Every configured variable, ordered by group then name.
	/// </summary>
	public async Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
			await using var command = _dataSource.CreateCommand(ArchiveStatements.PenCatalog, connection);
			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			var pens = new List<Pen>();

			while (await reader.ReadAsync().ConfigureAwait(false))
			{
				pens.Add(ReadPen(reader));
			}

			return Result.Ok<IReadOnlyList<Pen>>(pens);
		}
		catch (Exception exception)
		{
			var error = await MapAsync(exception, ArchiveStatements.TagCatalogRelation).ConfigureAwait(false);

			return Result.Fail<IReadOnlyList<Pen>>(error);
		}
	}

	/// <summary>
	/// A window of one layer for the pens the caller asks for, folded into one envelope per pen that has
	/// rows. A window holding no rows at all is a successful empty list rather than a failure.
	/// <para>
	/// A pen with nothing in the window gets no envelope. That rule is <b>interim</b>:
	/// <c>postgres-gap-reconstruction</c> revises it with a pre-window seed lookup, and
	/// <c>postgres-startup-and-composition</c> owns the consumer side, where a pen dropped from one window's
	/// result still carries the previous window's envelope. See docs/architecture/data-integration.md.
	/// </para>
	/// </summary>
	public async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		// The target guard turns a fault that would otherwise be intermittent into a deterministic one —
		// the decimator is only reached when a pen has rows, so a target below one succeeds on an empty
		// window and fails on a full one.
		var arguments = ValidateArguments(penIds, fromUtc, toUtc, targetColumnCount);

		if (arguments.IsFailed)
		{
			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(arguments.Errors);
		}

		// The layer guard sits behind the Result-returning checks rather than ahead of them, so that a
		// caller supplying two bad arguments at once gets the same answer here as from
		// RandomStubDataProvider, which reaches its own layer guard inside ToPointSpacing only after its
		// range and target checks.
		if (!Enum.IsDefined(layer))
		{
			throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown aggregation layer.");
		}

		var ids = arguments.Value;

		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
			await using var command = _dataSource.CreateCommand(ArchiveStatements.SparseHistoryWindow, connection);

			BindWindow(command, _timeConverter, ids, fromUtc, toUtc, layer);

			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			var rows = new List<HistoryRowFold.Row>();

			while (await reader.ReadAsync().ConfigureAwait(false))
			{
				rows.Add(ReadHistoryRow(reader));
			}

			return Result.Ok(HistoryRowFold.Fold(rows, _timeConverter, targetColumnCount));
		}
		catch (Exception exception)
		{
			// The statement touches one relation, so a 42P01 here can only mean trends.
			var error = await MapAsync(exception, ArchiveStatements.TrendsRelation).ConfigureAwait(false);

			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(error);
		}
	}

	/// <summary>
	/// The span the configured variables cover, in UTC. It is the span of the catalogue rather than of the
	/// archive: the statement is rooted at <c>semiplot_tags</c>, so an empty catalogue over an archive full
	/// of rows reports <see cref="ArchiveExtent.Empty"/>, the same answer a seeded catalogue over an empty
	/// <c>trends</c> gives. Both are successful reads — a null bound is a content state, not a failure.
	/// </summary>
	public async Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
			await using var command = _dataSource.CreateCommand(ArchiveStatements.ArchiveExtent, connection);
			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			return Result.Ok(await ReadExtentAsync(reader).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			// trends is the fallback rather than semiplot_tags: this statement touches both, and a missing
			// semiplot_tags would already be failing the catalogue read, while trends missing under a present
			// semiplot_tags is the earlier provisioning state only this read discovers.
			var error = await MapAsync(exception, ArchiveStatements.TrendsRelation).ConfigureAwait(false);

			return Result.Fail<ArchiveExtent>(error);
		}
	}

	// Internal rather than private so a unit test can bind through this exact path and compare the names it
	// produces against the statement's own tokens — the drift no fence extractor sees.
	internal static void BindWindow(
		NpgsqlCommand command,
		ArchiveTimeConverter timeConverter,
		int[] penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer)
	{
		command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
		{
			Value = penIds
		});
		command.Parameters.Add(new NpgsqlParameter("layer", NpgsqlDbType.Smallint) { Value = (short)layer });
		command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Timestamp)
		{
			Value = timeConverter.ToArchiveLocal(fromUtc)
		});
		command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Timestamp)
		{
			Value = timeConverter.ToArchiveLocal(toUtc)
		});
	}

	// The window bounds and the target column count carry the wording RandomStubDataProvider already uses,
	// so the same input reads the same way whichever implementation answers it. The identifiers narrow to
	// the integer trends.id holds, and the narrowing is range-tested rather than a silent wrap: an
	// identifier no int4 column can carry would otherwise select a different pen's rows.
	private static Result<int[]> ValidateArguments(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		int targetColumnCount)
	{
		if (fromUtc > toUtc)
		{
			return Result.Fail<int[]>($"Invalid range: fromUtc ({fromUtc:O}) is after toUtc ({toUtc:O}).");
		}

		if (targetColumnCount < 1)
		{
			return Result.Fail<int[]>($"Invalid target column count: {targetColumnCount} (must be at least one).");
		}

		var ids = new int[penIds.Count];

		for (var index = 0; index < penIds.Count; index++)
		{
			var penId = penIds[index];

			if (penId is < int.MinValue or > int.MaxValue)
			{
				return Result.Fail<int[]>(
					$"Invalid pen identifier: {penId} (must fit the archive's 32-bit identifier column).");
			}

			ids[index] = (int)penId;
		}

		return Result.Ok(ids);
	}

	// A plain projection of the columns: the fold owns the conversion, so the naive timestamp crosses
	// unchanged.
	private static HistoryRowFold.Row ReadHistoryRow(NpgsqlDataReader reader)
	{
		return new HistoryRowFold.Row(
			reader.GetInt32(0),
			reader.GetDateTime(1),
			reader.IsDBNull(2) ? null : reader.GetDouble(2));
	}

	// The id column is integer, so it is read with GetInt32 and widened — GetInt64 throws
	// InvalidCastException on an int4.
	private Pen ReadPen(NpgsqlDataReader reader)
	{
		var penId = (long)reader.GetInt32(0);

		return new Pen(
			penId,
			reader.GetString(1),
			reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
			reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
			PenLineStyleReader.Read(reader.GetInt16(4), penId, _logger));
	}

	// The archive stores naive local wall-clock time, so each bound crosses out through the converter
	// exactly once.
	private async Task<ArchiveExtent> ReadExtentAsync(NpgsqlDataReader reader)
	{
		if (!await reader.ReadAsync().ConfigureAwait(false))
		{
			return ArchiveExtent.Empty;
		}

		if (reader.IsDBNull(0) || reader.IsDBNull(1))
		{
			return ArchiveExtent.Empty;
		}

		return new ArchiveExtent(
			_timeConverter.ToUtc(reader.GetDateTime(0)),
			_timeConverter.ToUtc(reader.GetDateTime(1)));
	}

	// The probe's answer, or this read's own fallback relation when it has none, fills
	// ArchiveNotInitialisedError.Table, which consumers route on and which can never be empty.
	private async Task<Error> MapAsync(Exception exception, string fallbackRelation)
	{
		if (exception is PostgresException { SqlState: PostgresErrorCodes.UndefinedTable })
		{
			var missingRelation = await _missingRelationProbe.FindMissingRelationAsync().ConfigureAwait(false);

			return _exceptionMapper.Map(exception, missingRelation ?? fallbackRelation);
		}

		var error = _exceptionMapper.Map(exception);

		// A read that fails with no server answer behind it — a null reference or a bad cast inside the row
		// read — is a fault in this code, and ArchiveReadFailedError alone dresses it as a server state. It
		// still crosses typed, because nothing may escape the boundary; the log is where it stays visible.
		if (error is ArchiveReadFailedError { SqlState.Length: 0 })
		{
			_logger.LogError(exception, "The archive read failed with an exception the provider did not expect.");
		}

		return error;
	}
}
