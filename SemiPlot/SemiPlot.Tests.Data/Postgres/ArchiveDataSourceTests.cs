using Npgsql;

using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// The command bound, without a server. Constructing an NpgsqlDataSource opens nothing and
// NpgsqlConnection.CreateCommand works on a closed connection, so the resolved CommandTimeout is readable
// here.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveDataSourceTests
{
	private const int BackstopSeconds = 300;

	// The bound no longer depends on anything read from the server, so every command carries the same one
	// from the first read of a process onwards.
	[Fact]
	public void EveryCommandCarriesTheFixedBackstop()
	{
		using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		using var connection = new NpgsqlConnection();
		using var command = dataSource.CreateCommand("SELECT 1;", connection);

		Assert.Equal(BackstopSeconds, command.CommandTimeout);
	}
}
