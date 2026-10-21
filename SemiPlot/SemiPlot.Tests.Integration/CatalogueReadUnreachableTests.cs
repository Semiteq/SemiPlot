using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class CatalogueReadUnreachableTests
{
	private static readonly PostgresConnectionSettings _settings = UnreachableArchive.Settings();

	[Fact]
	public async Task ACatalogueReadOverARefusedSocket_AnswersUnreachableNamingTheArchive()
	{
		await using var services = UnreachableArchive.Services(_settings);

		var read = await services.GetRequiredService<IDataProvider>().QueryPensAsync();

		read.IsFailed.Should().BeTrue();
		var error = read.Errors.Should().ContainSingle().Which.Should().BeOfType<ArchiveError>().Which;
		error.Kind.Should().Be(ArchiveFault.Unreachable);
		error.Host.Should().Be(_settings.Host);
		error.Port.Should().Be(_settings.Port);
		error.Database.Should().Be(_settings.Database);
	}
}
