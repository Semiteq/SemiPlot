using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads the archive over the pooled <see cref="ArchiveDataSource"/>. <see cref="QueryPensAsync"/> answers
/// the configured variables and <see cref="QueryArchiveExtentAsync"/> the span they cover, both crossing
/// the boundary in UTC. Every failure leaves through the public error vocabulary — nothing internal crosses
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

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		return Task.FromResult(Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(
			new ProviderNotImplementedError(nameof(QueryHistoryAsync))));
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
