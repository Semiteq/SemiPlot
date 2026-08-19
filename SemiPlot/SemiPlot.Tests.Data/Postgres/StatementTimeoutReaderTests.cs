using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// The parse alone, plus the swallow. Issuing the read needs a database and is covered by the gated tests;
// turning the server's answer into a bound is pure logic, and a server that cannot be reached at all is
// reachable here because ConnectionSettingsFactory points at an address nothing answers.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class StatementTimeoutReaderTests
{
	[Theory]
	[InlineData("30000", 30000)]
	[InlineData("1000", 1000)]
	[InlineData("500", 500)]
	[InlineData("120000", 120000)]
	public void AParsedSettingIsTheServersBoundInMilliseconds(string setting, int expected)
	{
		Assert.Equal(expected, StatementTimeoutReader.ParseMilliseconds(setting));
	}

	// A server bounding nothing, and a server whose answer does not parse at all, are the same state: no
	// bound to report. The two are told apart in the log, not in the returned value.
	[Theory]
	[InlineData("0")]
	[InlineData("30s")]
	[InlineData("-1")]
	[InlineData("")]
	[InlineData(null)]
	public void AnUnboundedOrUnparsableSettingReadsAsZero(string? setting)
	{
		Assert.Equal(0, StatementTimeoutReader.ParseMilliseconds(setting));
	}

	// The reader runs on the failure path, so its own failure has to answer null rather than throw a second
	// exception into the mapping the first one is still waiting on.
	[Fact]
	public async Task AServerThatCannotBeReachedAnswersNoBound()
	{
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var reader = new StatementTimeoutReader(dataSource, NullLogger<StatementTimeoutReader>.Instance);

		Assert.Null(await reader.ReadEffectiveBoundAsync());
	}
}
