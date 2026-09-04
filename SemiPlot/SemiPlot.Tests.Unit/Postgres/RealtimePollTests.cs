using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// The failure ladder alone, which needs no server: ConnectionSettingsFactory points at an address nothing
// answers, so every tick here fails on the connect attempt. What a successful tick reads is a database
// question and is covered by RealtimePollReadTests.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class RealtimePollTests
{
	private static readonly int[] _penIds = [1, 2];

	// Restated here rather than read off the type: the threshold is a promise to the operator, and a test
	// computing it from the constant it guards would pass whatever the constant became.
	private const int ConsecutiveFailuresBeforeFault = 3;

	[Fact]
	public async Task AFailingTickReportsNoSampleAndNoException()
	{
		await using var dataSource = NpgsqlDataSource.Create(ConnectionSettingsFactory.Create().ConnectionString);

		var tick = await NewPoll(dataSource).ReadOnceAsync(TestContext.Current.CancellationToken);

		tick.Samples.Should().BeEmpty();
	}

	// One failure is a dropped packet or a recycled pool connection, not a fault: Npgsql opens a fresh
	// physical connection after a reset, so a fault raised on one would flap over a healthy archive.
	[Fact]
	public async Task ASingleFailureRaisesNoFault()
	{
		await using var dataSource = NpgsqlDataSource.Create(ConnectionSettingsFactory.Create().ConnectionString);

		var tick = await NewPoll(dataSource).ReadOnceAsync(TestContext.Current.CancellationToken);

		tick.StateChange.Should().BeNull();
	}

	[Fact]
	public async Task TheThirdConsecutiveFailureRaisesExactlyOneFault()
	{
		await using var dataSource = NpgsqlDataSource.Create(ConnectionSettingsFactory.Create().ConnectionString);

		var states = await TickAsync(NewPoll(dataSource), ConsecutiveFailuresBeforeFault);

		states.Take(ConsecutiveFailuresBeforeFault - 1).Should().AllSatisfy(state => state.Should().BeNull());

		var lastFault = states[^1]?.Fault;
		var fault = lastFault.Should().BeOfType<ArchiveError>().Which;

		fault.Kind.Should().Be(ArchiveFault.ConnectionLost);
		fault.Detail.Should().Be($"{ConsecutiveFailuresBeforeFault}");
	}

	// The fault carries the address the failing ticks were issued against, so the operator reads which
	// archive stopped answering rather than only that one did.
	[Fact]
	public async Task TheRaisedFaultNamesTheArchiveItWasIssuedAgainst()
	{
		var settings = ConnectionSettingsFactory.Create();

		await using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);

		var states = await TickAsync(NewPoll(dataSource), ConsecutiveFailuresBeforeFault);

		var lastFault = states[^1]?.Fault;
		var fault = lastFault.Should().BeOfType<ArchiveError>().Which;

		fault.Host.Should().Be(settings.Host);
		fault.Port.Should().Be(settings.Port);
		fault.Database.Should().Be(settings.Database);
	}

	// A flag, not the counter, stops the second banner: a failure run is one fault, and the number it
	// carries is the threshold that raised it, never the run's length (five failures here, three reported).
	[Fact]
	public async Task AFourthAndFifthConsecutiveFailureRaiseNothingFurther()
	{
		await using var dataSource = NpgsqlDataSource.Create(ConnectionSettingsFactory.Create().ConnectionString);

		var states = await TickAsync(NewPoll(dataSource), ConsecutiveFailuresBeforeFault + 2);

		states.Take(ConsecutiveFailuresBeforeFault - 1).Should().AllSatisfy(state => state.Should().BeNull());

		var thirdFault = states[ConsecutiveFailuresBeforeFault - 1]?.Fault;
		var fault = thirdFault.Should().BeOfType<ArchiveError>().Which;

		fault.Detail.Should().Be($"{ConsecutiveFailuresBeforeFault}");
		states.Skip(ConsecutiveFailuresBeforeFault).Should().AllSatisfy(state => state.Should().BeNull());
	}

	// The Rx wrapper subscribes a consumer that carries no error handler, so an escaping exception would
	// go unhandled on the UI scheduler rather than reaching anyone who could report it.
	[Fact]
	public async Task NoExceptionEscapesAFailingTick()
	{
		await using var dataSource = NpgsqlDataSource.Create(ConnectionSettingsFactory.Create().ConnectionString);

		var poll = NewPoll(dataSource);

		var failures = await Record.ExceptionAsync(
			() => TickAsync(poll, ConsecutiveFailuresBeforeFault + 2));

		failures.Should().BeNull();
	}

	// A failing tick never establishes a baseline, so the poll stays on the baseline branch and the next
	// tick has nothing to bind @lastSeen to.
	[Fact]
	public async Task AFailingTickLeavesTheBaselineUnread()
	{
		await using var dataSource = NpgsqlDataSource.Create(ConnectionSettingsFactory.Create().ConnectionString);

		var poll = NewPoll(dataSource);

		await TickAsync(poll, ConsecutiveFailuresBeforeFault);

		poll.LastSeen.Should().BeNull();
	}

	// A tick must not inherit the connection string's five-minute backstop, or a server that accepts and
	// then answers nothing would freeze the chart for minutes before the fault threshold banner shows.
	[Theory]
	[InlineData(ArchiveStatements.RealtimeBaseline)]
	[InlineData(ArchiveStatements.RealtimePoll)]
	public void ATicksStatementCarriesItsOwnBoundAndNotTheDataSourceBackstop(string statementText)
	{
		const int TickBoundSeconds = 10;
		const int DataSourceBackstopSeconds = 300;

		using var connection = new NpgsqlConnection();

		using var command = RealtimePoll.CreateTickCommand(statementText, connection);

		command.CommandTimeout.Should().Be(TickBoundSeconds);
		(command.CommandTimeout < DataSourceBackstopSeconds).Should().BeTrue();
	}

	[Fact]
	public void TheEngineRejectsANullIdentifierList()
	{
		var settings = ConnectionSettingsFactory.Create();

		using var dataSource = NpgsqlDataSource.Create(settings.ConnectionString);

		var act = () => new RealtimePoll(
			dataSource,
			new ArchiveTimeConverter(settings.SourceTimeZone),
			new ArchiveExceptionMapper(settings),
			settings,
			null!,
			NullLogger.Instance);

		act.Should().Throw<ArgumentNullException>();
	}

	private static async Task<IReadOnlyList<ArchiveConnectionState?>> TickAsync(RealtimePoll poll, int tickCount)
	{
		var states = new List<ArchiveConnectionState?>();

		for (var tick = 0; tick < tickCount; tick++)
		{
			states.Add((await poll.ReadOnceAsync(TestContext.Current.CancellationToken)).StateChange);
		}

		return states;
	}

	private static RealtimePoll NewPoll(NpgsqlDataSource dataSource)
	{
		var settings = ConnectionSettingsFactory.Create();

		return new RealtimePoll(
			dataSource,
			new ArchiveTimeConverter(settings.SourceTimeZone),
			new ArchiveExceptionMapper(settings),
			settings,
			_penIds,
			NullLogger.Instance);
	}
}
