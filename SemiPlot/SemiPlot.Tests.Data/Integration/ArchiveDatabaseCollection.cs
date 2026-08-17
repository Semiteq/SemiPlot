using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// Every gated class joins this collection, so one server is started for the whole run and the classes
// that share it do not race each other over its databases.
[CollectionDefinition(Name)]
public sealed class ArchiveDatabaseCollection : ICollectionFixture<PostgresContainerFixture>
{
	public const string Name = "archive-database";
}
