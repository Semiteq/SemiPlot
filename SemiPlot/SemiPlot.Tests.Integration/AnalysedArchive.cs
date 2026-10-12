using Xunit;

namespace SemiPlot.Tests.Integration;

// One clone of the seeded template for the plan tests, analysed once. The template is built by COPY and
// never analysed, so the planner may pick a sequential scan with no stats. ANALYZE needs table ownership
// or MAINTAIN, which only the admin connection holds, and it writes no rows.
public sealed class AnalysedArchive(PostgresContainerFixture postgresContainerFixture) : IAsyncLifetime
{
	private const string AnalyseTrendsCommand = "ANALYZE public.trends;";

	private const string AnalyseCatalogCommand = "ANALYZE public.semiplot_tags;";

	private ArchiveDatabase? _archiveDatabase;

	public ArchiveDatabase Database =>
		_archiveDatabase ?? throw new InvalidOperationException(
			"The analysed archive was used before it was initialised.");

	public async ValueTask InitializeAsync()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		_archiveDatabase = await postgresContainerFixture.CloneAsync(
			CloneSource.Template,
			async clone =>
			{
				await ArchiveDatabase.ExecuteAsync(clone.AdminConnectionString, AnalyseTrendsCommand, cancellationToken);

				await ArchiveDatabase.ExecuteAsync(clone.AdminConnectionString, AnalyseCatalogCommand, cancellationToken);
			},
			cancellationToken);
	}

	public async ValueTask DisposeAsync()
	{
		if (_archiveDatabase is not null)
		{
			await _archiveDatabase.DisposeAsync();
		}
	}
}
