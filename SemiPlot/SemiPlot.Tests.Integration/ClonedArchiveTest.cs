using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

public abstract class ClonedArchiveTest(PostgresContainerFixture fixture, CloneSource source)
	: IAsyncLifetime
{
	// Bounds one awaited emission, so a regression fails the test instead of hanging the executable.
	public static readonly TimeSpan GateBound = TimeSpan.FromSeconds(30);

	private ArchiveDatabase? _archiveDatabase;

	protected PostgresContainerFixture Fixture => fixture;

	protected ArchiveDatabase Database =>
		_archiveDatabase ?? throw new InvalidOperationException("The archive was used before it was cloned.");

	public async ValueTask InitializeAsync()
	{
		// The seed reaches Database, so the clone is published before the seed runs. A seed that throws has
		// the clone dropped, and a lifecycle that throws is not owed a DisposeAsync.
		_archiveDatabase = await fixture.CloneAsync(source, database =>
		{
			_archiveDatabase = database;

			return SeedAsync().AsTask();
		});
	}

	protected virtual ValueTask SeedAsync()
	{
		return ValueTask.CompletedTask;
	}

	protected ArchiveWriter Writer()
	{
		return new ArchiveWriter(Database.WriterConnectionString);
	}

	public async ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);

		if (_archiveDatabase is null)
		{
			return;
		}

		var database = _archiveDatabase;

		_archiveDatabase = null;

		await database.DisposeAsync();
	}
}
