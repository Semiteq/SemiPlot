using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Tests.Data.Integration;

using Xunit;

namespace SemiPlot.Tests.Journeys;

// The one test that proves the container harness reaches across the assembly boundary before a journey
// depends on it: this assembly's own collection definition starts the server, the seeded template built
// in SemiPlot.Tests.Data is cloned, and the clone answers a read through the real provider registration.
// A journey failing after this one passes is a fault in the journey, not in the harness.
[Collection(ArchiveJourneyCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveHarnessSmokeTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	[Fact]
	public async Task TheClonedTemplateAnswersAnExtentReadThroughTheRealProvider()
	{
		postgresContainerFixture.RequireAvailable();

		await using var services = ArchiveProviderFactory.Build(seededArchive.Database.ReaderConnectionString);

		var result = await services.GetRequiredService<IDataProvider>().QueryArchiveExtentAsync();

		result.IsSuccess.Should().BeTrue(ArchiveReadSupport.Describe(result));
		result.Value.IsEmpty.Should().BeFalse();
		result.Value.FirstUtc.Should().BeBefore(result.Value.LastUtc);
	}
}
