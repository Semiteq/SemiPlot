using AwesomeAssertions;

using Microsoft.Extensions.Logging.Testing;

using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// A failed tick and its logged run of failures, which needs no server: every tick connects to an address
// nothing answers and fails there. What a successful tick reads is a database question and is covered by
// RealtimePollReadTests.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimePollTests
{
	private const int TickCount = 5;

	private static readonly PostgresConnectionSettings _settings = UnreachableArchive.Settings(TimeSpan.FromSeconds(1));

	[Fact]
	public async Task AFailedTickEmitsNoSample()
	{
		await using var services = UnreachableArchive.Services(_settings);

		var samples = await NewPoll(services).ReadOnceAsync(TestContext.Current.CancellationToken);

		samples.Should().BeEmpty();
	}

	[Fact]
	public async Task EachFailedTickLogsTheRunOfFailuresItExtends()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		await using var services = UnreachableArchive.Services(_settings);
		using var logs = new FakeLoggerProvider();

		var poll = NewPoll(services, logs.CreateLogger("RealtimePoll"));

		for (var tick = 0; tick < TickCount; tick++)
		{
			await poll.ReadOnceAsync(cancellationToken);
		}

		logs.Collector.GetSnapshot()
			.Select(entry => entry.GetStructuredStateValue(ArmedGate.FailureRunKey))
			.Should().Equal("1", "2", "3", "4", "5");
	}
}
