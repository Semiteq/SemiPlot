using SemiPlot.Tests.Data.Integration;

using Xunit;

namespace SemiPlot.Tests.Journeys;

// The journeys' own collection definition. xunit v3 discovers [CollectionDefinition] per test assembly,
// so ArchiveDatabaseCollection in SemiPlot.Tests.Data does not bind here; the fixture type itself is
// public and crosses the reference unchanged, and one server is started for this assembly's run.
[CollectionDefinition(Name)]
public sealed class ArchiveJourneyCollection : ICollectionFixture<PostgresContainerFixture>
{
	public const string Name = "archive-journey";
}
