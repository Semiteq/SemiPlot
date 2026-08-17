using System.Globalization;

using Npgsql;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The stale sweep is the one statement in this slice that destroys, so what it selects is asserted
// rather than trusted. Each planted database stands for one way the predicate can be wrong: the prefix
// read as a LIKE pattern, a template another run is still using, a name a foreign principal chose, and
// a template a concurrent run has just stamped and is about to seed.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveTemplateTests(PostgresContainerFixture postgresContainerFixture)
{
	public enum Stamp
	{
		None,
		Fresh,
		Stale
	}

	// One row per way the predicate can be wrong.
	[Theory]
	[InlineData(ArchiveTemplate.NamePrefix, Stamp.Stale, false, false)]
	[InlineData("semiplot_benchx", Stamp.Stale, false, true)]
	[InlineData(ArchiveTemplate.NamePrefix + "\"", Stamp.Stale, false, false)]
	[InlineData(ArchiveTemplate.NamePrefix + "busy", Stamp.Stale, true, true)]
	[InlineData(ArchiveTemplate.NamePrefix + "live", Stamp.Fresh, false, true)]
	[InlineData(ArchiveTemplate.NamePrefix + "bare", Stamp.None, false, true)]
	public async Task TheStaleSweepDropsIdleDatedTemplatesAndNothingElse(
		string namePrefix,
		Stamp stamp,
		bool holdsSession,
		bool survives)
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var name = namePrefix + Guid.NewGuid().ToString("N")[..12];

		await CreateAsync(name, cancellationToken);

		try
		{
			await StampAsync(name, stamp, cancellationToken);
			await SweepAsync(name, holdsSession, cancellationToken);

			Assert.Equal(survives, await ExistsAsync(name, cancellationToken));
		}
		finally
		{
			ArchiveDatabase.ClearPool(postgresContainerFixture.Server.AdminConnectionStringFor(name));

			await DropAsync(name, cancellationToken);
		}
	}

	// The template this run holds is excluded by name, so a sweep never takes the database the rest of
	// the suite clones from.
	[Fact]
	public async Task TheSweepLeavesTheTemplateThisRunIsUsing()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		await ArchiveTemplate.DropStaleAsync(postgresContainerFixture.Server, cancellationToken);

		Assert.True(await ExistsAsync(ArchiveTemplate.Name, cancellationToken));
	}

	// A live session stands for another run still holding its template.
	private async Task SweepAsync(string name, bool holdsSession, CancellationToken cancellationToken)
	{
		var server = postgresContainerFixture.Server;

		if (!holdsSession)
		{
			await ArchiveTemplate.DropStaleAsync(server, cancellationToken);

			return;
		}

		await using var session = new NpgsqlConnection(server.AdminConnectionStringFor(name));

		await session.OpenAsync(cancellationToken);
		await ArchiveTemplate.DropStaleAsync(server, cancellationToken);
	}

	// The stamp the sweep reads, placed the way ArchiveTemplate places it but offset from the server's
	// clock, so a template can stand for a run that finished long ago or for one still in flight.
	private Task StampAsync(string name, Stamp stamp, CancellationToken cancellationToken)
	{
		if (stamp == Stamp.None)
		{
			return Task.CompletedTask;
		}

		var offsetSeconds = stamp == Stamp.Stale
			? -((long)ArchiveTemplate.StaleAfter.TotalSeconds + 60L)
			: 0L;

		return ArchiveDatabase.ExecuteAsync(
			postgresContainerFixture.Server.AdminConnectionString,
			$"""
			DO
			$stamp$
			BEGIN
			  EXECUTE format(
			    'COMMENT ON DATABASE %I IS %L',
			    {Literal(name)},
			    '{ArchiveTemplate.MarkerPrefix}'
			      || (extract(epoch from now())::bigint + {offsetSeconds.ToString(CultureInfo.InvariantCulture)}));
			END
			$stamp$;
			""",
			cancellationToken);
	}

	private Task CreateAsync(string name, CancellationToken cancellationToken)
	{
		return ArchiveDatabase.ExecuteAsync(
			postgresContainerFixture.Server.AdminConnectionString,
			$"CREATE DATABASE {Quote(name)} TEMPLATE template0 ENCODING 'UTF8' LC_COLLATE 'C' LC_CTYPE 'C';",
			cancellationToken);
	}

	private Task DropAsync(string name, CancellationToken cancellationToken)
	{
		return ArchiveDatabase.ExecuteAsync(
			postgresContainerFixture.Server.AdminConnectionString,
			$"DROP DATABASE IF EXISTS {Quote(name)} WITH (FORCE);",
			cancellationToken);
	}

	private async Task<bool> ExistsAsync(string name, CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(postgresContainerFixture.Server.AdminConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(ArchiveDatabase.CountDatabasesCommand, connection);

		command.Parameters.AddWithValue("name", name);

		return (long)(await command.ExecuteScalarAsync(cancellationToken))! > 0L;
	}

	private static string Quote(string name)
	{
		return $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
	}

	private static string Literal(string value)
	{
		return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
	}
}
