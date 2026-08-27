using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The health read against a real archive, both ways round: a seeded template answers with nothing, and a
// single row in the default partition answers with the one warning that names it.
//
// Every test clones the template for itself rather than taking SeededArchive, whose contract is that a class
// leaves the database as it found it. One of these writes a row that no later read may see, and a clone per
// test is what keeps that row out of every other class's counts. The clone is cheap next to the seeding COPY
// it copies from.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ArchiveHealthReadTests(PostgresContainerFixture postgresContainerFixture)
{
	// Far outside the seeded span, so no daily partition covers it and the row can only land in the default
	// partition. That is exactly the state the SCADA produces when it fails to create the day it is writing
	// into, reached here without having to break the partition set.
	private const string RowMissingItsDay = """
		INSERT INTO public.trends (id, l, t, v, q) VALUES (1, 0, '2001-01-01 00:00:00', 1.0, 0);
		""";

	// The template carries the seeded day's rows in its day partitions, so an empty answer here is also what
	// proves the ONLY qualifier: without it those rows would answer for the default partition and a healthy
	// archive would report the fault on every start.
	[Fact]
	public async Task ASeededArchiveAnswersWithNoHealthWarning()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		await using var database = await postgresContainerFixture.CloneTemplateAsync(cancellationToken);
		await using var services = ArchiveProviderFactory.Build(database.ReaderConnectionString);

		var warnings = await services.GetRequiredService<ArchiveHealthReader>().ReadAsync(cancellationToken);

		Assert.Empty(warnings);
	}

	[Fact]
	public async Task ARowInTheDefaultPartitionIsReportedAsOneWarningNamingIt()
	{
		postgresContainerFixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		await using var database = await postgresContainerFixture.CloneTemplateAsync(cancellationToken);

		await ArchiveDatabase.ExecuteAsync(database.AdminConnectionString, RowMissingItsDay, cancellationToken);

		await using var services = ArchiveProviderFactory.Build(database.ReaderConnectionString);

		var warnings = await services.GetRequiredService<ArchiveHealthReader>().ReadAsync(cancellationToken);

		var warning = Assert.IsType<ArchiveDefaultPartitionNotEmptyError>(Assert.Single(warnings));

		Assert.Equal(database.Name, warning.Database);
		Assert.Equal(ArchiveStatements.DefaultPartitionRelation, warning.Partition);
		Assert.Contains(warning.Partition, warning.Message);
	}
}
