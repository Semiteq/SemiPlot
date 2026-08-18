using Microsoft.Extensions.Logging.Abstractions;

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
	private const int FallbackSeconds = 300;

	[Theory]
	[InlineData("30000", 30000, 60)]
	[InlineData("1000", 1000, 31)]
	[InlineData("500", 500, 31)]
	[InlineData("120000", 120000, 150)]
	public void AParsedBoundBecomesTheServersBoundPlusTheMargin(
		string setting,
		int expectedBoundMilliseconds,
		int expectedCommandTimeoutSeconds)
	{
		using var dataSource = new ArchiveDataSource(
			ConnectionSettingsFactory.Create(),
			NullLogger<ArchiveDataSource>.Instance);

		dataSource.CacheEffectiveStatementTimeout(setting);

		Assert.Equal(TimeSpan.FromMilliseconds(expectedBoundMilliseconds), dataSource.EffectiveStatementTimeout);
		Assert.Equal(expectedCommandTimeoutSeconds, CommandTimeoutOf(dataSource));
	}

	// A server bounding nothing, and a server whose answer does not parse at all, are the same state: the
	// command takes a fixed bound of its own rather than Command Timeout=0's unbounded wait.
	[Theory]
	[InlineData("0")]
	[InlineData("30s")]
	[InlineData("")]
	[InlineData(null)]
	public void AnUnboundedOrUnparsableServerTakesTheFixedFallback(string? setting)
	{
		using var dataSource = new ArchiveDataSource(
			ConnectionSettingsFactory.Create(),
			NullLogger<ArchiveDataSource>.Instance);

		dataSource.CacheEffectiveStatementTimeout(setting);

		Assert.Equal(TimeSpan.Zero, dataSource.EffectiveStatementTimeout);
		Assert.Equal(FallbackSeconds, CommandTimeoutOf(dataSource));
	}

	// Unset is not zero: before any physical connection has opened there is no server answer to derive a
	// bound from, and the command takes the same fixed fallback without claiming the server bounds nothing.
	[Fact]
	public void ABoundNotYetKnownTakesTheFixedFallback()
	{
		using var dataSource = new ArchiveDataSource(
			ConnectionSettingsFactory.Create(),
			NullLogger<ArchiveDataSource>.Instance);

		Assert.Null(dataSource.EffectiveStatementTimeout);
		Assert.Equal(FallbackSeconds, CommandTimeoutOf(dataSource));
	}

	[Fact]
	public void ALaterAnswerReplacesTheCachedBound()
	{
		using var dataSource = new ArchiveDataSource(
			ConnectionSettingsFactory.Create(),
			NullLogger<ArchiveDataSource>.Instance);

		dataSource.CacheEffectiveStatementTimeout("30000");
		dataSource.CacheEffectiveStatementTimeout("0");

		Assert.Equal(TimeSpan.Zero, dataSource.EffectiveStatementTimeout);
		Assert.Equal(FallbackSeconds, CommandTimeoutOf(dataSource));
	}

	private static int CommandTimeoutOf(ArchiveDataSource dataSource)
	{
		using var connection = new NpgsqlConnection();
		using var command = dataSource.CreateCommand("SELECT 1;", connection);

		return command.CommandTimeout;
	}
}
