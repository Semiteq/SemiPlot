using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

public abstract class ClonedArchiveTest(PostgresContainerFixture fixture, CloneSource source)
	: IAsyncLifetime
{
	private ArchiveDatabase? _archiveDatabase;

	protected PostgresContainerFixture Fixture => fixture;

	protected ArchiveDatabase Database =>
		_archiveDatabase ?? throw new InvalidOperationException("The archive was used before it was cloned.");

	public async ValueTask InitializeAsync()
	{
		_archiveDatabase = source is CloneSource.Template
			? await fixture.CloneTemplateAsync()
			: await fixture.CloneProvisionedAsync();

		try
		{
			await SeedAsync();
		}
		// The clone already exists by now, and a lifecycle that throws is not owed a DisposeAsync. Dropping
		// it here is what keeps a failed seeding from leaking a database and from being reported a second
		// time, differently worded, by the fixture's own leave-nothing-behind check.
		catch
		{
			await DisposeAsync();

			throw;
		}
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
		if (_archiveDatabase is null)
		{
			return;
		}

		var database = _archiveDatabase;

		_archiveDatabase = null;

		await database.DisposeAsync();
	}
}
