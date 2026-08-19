using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The one gated test over the reported statement_timeout. It proves the number in
// ArchiveQueryTimedOutError.Timeout is the server's own bound rather than the fixed client backstop, which is
// the operator-visible guarantee the lazy StatementTimeoutReader had to preserve.
//
// The bound sits inside a bracket, because the reader opens a session of the same reader role and therefore
// runs under the same bound: it has to be above the reader's own pg_settings read and below the forced read.
// Both ends are measured (see the bound below), but a bracket is a property of the machine it runs on, so every
// outcome still names which end it accuses instead of failing on a bare assertion — a bracket that does not
// open has to be readable from the CI log alone.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class StatementTimeoutReadTests(PostgresContainerFixture postgresContainerFixture)
{
	// Measured against the template on a postgres:17-alpine container: the pg_settings read, which materialises
	// pg_show_all_settings() and its few hundred rows, takes 1.6 to 1.8 ms warm and 4.2 ms cold, and the full
	// seeded day at the raw layer for all eight pens takes 327 to 343 ms warm and 548 ms cold. Fifty milliseconds
	// therefore clears the settings read by about twelve times at its worst and sits about six times under the
	// forced read — a working bracket, though a narrower one on the upper end than 'orders of magnitude'.
	private const int BoundMilliseconds = 50;

	// Never reached: the read fails in the server before a row is folded. It is stated only because
	// QueryHistoryAsync rejects a target below one ahead of opening a connection.
	private const int TargetColumnCount = 4096;

	private static readonly TimeSpan _bound = TimeSpan.FromMilliseconds(BoundMilliseconds);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	// Selecting the pens costs nothing, unlike generating their rows: the forced read needs the identifiers
	// only, and the assertion is over the error rather than over any row.
	private static readonly Lazy<IReadOnlyList<long>> _seededPenIds = new(SelectSeededPenIds);

	[Fact]
	public async Task TimedOutReadReportsTheServersOwnBound()
	{
		postgresContainerFixture.RequireAvailable();

		await using var database = await postgresContainerFixture.CloneTemplateAsync(
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			BoundCommandFor(database.Name),
			TestContext.Current.CancellationToken);

		var result = await ReadTheWholeSeededDayAsync(database.ReaderConnectionString);

		if (result.IsSuccess)
		{
			Assert.Fail(
				$"The full seeded day at the raw layer for every pen completed inside the {BoundMilliseconds} ms "
					+ $"bound and returned {result.Value.Count} envelopes, so the bracket's upper end is wrong "
					+ "and the forcing mechanism no longer forces a 57014. Change the forcing mechanism — never "
					+ "widen the assertion.");
		}

		var timedOut = result.Errors.OfType<ArchiveQueryTimedOutError>().FirstOrDefault();

		if (timedOut is null)
		{
			Assert.Fail(
				$"The forced read failed with {DescribeErrorTypes(result)} rather than "
					+ $"{nameof(ArchiveQueryTimedOutError)}: {ArchiveReadSupport.Describe(result)}");
		}

		if (timedOut.Timeout == TimeSpan.Zero)
		{
			Assert.Fail(
				"The forced read timed out as intended, but no bound came back, so the reader's own pg_settings "
					+ $"read was itself cut by the {BoundMilliseconds} ms bound and the bracket's lower end is "
					+ "wrong. Raise the bound above the settings read rather than lowering it.");
		}

		// The client backstop is five minutes and an unreadable bound is zero, so equality here can hold only
		// for the number the server itself applied.
		Assert.Equal(_bound, timedOut.Timeout);
	}

	// Role-scoped rather than database-scoped, and that is not a preference: semibase create sets the 30 s
	// bound on semiplot_reader, and PostgreSQL applies database settings before role settings, so an
	// ALTER DATABASE ... SET would lose to the role default and the read would never trip. The fixture's admin
	// connection is the superuser, so the statement is permitted. Neither value is a foreign principal's — the
	// role is a constant and the database name is the clone's own prefix plus a hex suffix.
	private static string BoundCommandFor(string database)
	{
		return FormattableString.Invariant(
			$"""
			ALTER ROLE "{SemibaseProvisioner.ReaderRole}" IN DATABASE "{database}"
			  SET statement_timeout = '{BoundMilliseconds}ms';
			""");
	}

	private static async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> ReadTheWholeSeededDayAsync(
		string connectionString)
	{
		await using var services = ArchiveProviderFactory.Build(connectionString);

		// The whole generated span, raw layer, every seeded pen: 229 862 rows, which is what carries the read
		// to 327 to 548 ms, six to eleven times above the bound. The extent read is far too fast to trip it
		// reliably.
		return await services.GetRequiredService<IDataProvider>().QueryHistoryAsync(
			_seededPenIds.Value,
			_timeConverter.ToUtc(ArchiveTemplate.Slice.Start),
			_timeConverter.ToUtc(ArchiveTemplate.Slice.End),
			AggregationLayer.Raw,
			TargetColumnCount);
	}

	private static string DescribeErrorTypes<T>(Result<T> result)
	{
		return string.Join(", ", result.Errors.Select(error => error.GetType().Name));
	}

	private static IReadOnlyList<long> SelectSeededPenIds()
	{
		return RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Select(pen => pen.PenId)
			.ToArray();
	}
}
