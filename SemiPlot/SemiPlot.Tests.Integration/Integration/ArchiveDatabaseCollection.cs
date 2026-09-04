using Xunit;

namespace SemiPlot.Tests.Integration;

// Every container test joins this collection, so one server is started for the whole run. The classes
// share that server and nothing else: each works on its own clone, which is an independent database.
[CollectionDefinition(Name)]
public sealed class ArchiveDatabaseCollection : ICollectionFixture<PostgresContainerFixture>
{
	public const string Name = "archive-database";
}
