using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// One clone of the seeded template for a whole test class. The counts the tests assert are the
// template's, so every test in the class must see the same database and must leave it as it found it —
// a leaked row would corrupt the next test's count.
public sealed class SeededArchive(PostgresContainerFixture postgresContainerFixture) : IAsyncLifetime
{
	private ArchiveDatabase? _archiveDatabase;

	public ArchiveDatabase Database =>
		_archiveDatabase ?? throw new InvalidOperationException(
			postgresContainerFixture.UnavailableReason ?? "The seeded archive was used before it was initialised.");

	public async ValueTask InitializeAsync()
	{
		if (postgresContainerFixture.IsAvailable)
		{
			_archiveDatabase = await postgresContainerFixture.CloneTemplateAsync();
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_archiveDatabase is not null)
		{
			await _archiveDatabase.DisposeAsync();
		}
	}
}
