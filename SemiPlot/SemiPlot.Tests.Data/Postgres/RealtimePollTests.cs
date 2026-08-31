using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

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
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var tick = await NewPoll(dataSource).ReadOnceAsync(TestContext.Current.CancellationToken);

		Assert.Empty(tick.Samples);
	}

	// One failure is a dropped packet or a recycled pool connection, not a fault: Npgsql opens a fresh
	// physical connection after a reset, so a fault raised on one would flap over a healthy archive.
	[Fact]
	public async Task ASingleFailureRaisesNoFault()
	{
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var tick = await NewPoll(dataSource).ReadOnceAsync(TestContext.Current.CancellationToken);

		Assert.Null(tick.StateChange);
	}

	[Fact]
	public async Task TheThirdConsecutiveFailureRaisesExactlyOneFault()
	{
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var states = await TickAsync(NewPoll(dataSource), ConsecutiveFailuresBeforeFault);

		Assert.All(states.Take(ConsecutiveFailuresBeforeFault - 1), Assert.Null);

		var fault = Assert.IsType<ArchiveError>(states[^1]?.Fault);

		Assert.Equal(ArchiveFault.ConnectionLost, fault.Kind);
		Assert.Equal($"{ConsecutiveFailuresBeforeFault}", fault.Detail);
	}

	// The fault carries the address the failing ticks were issued against, so the operator reads which
	// archive stopped answering rather than only that one did.
	[Fact]
	public async Task TheRaisedFaultNamesTheArchiveItWasIssuedAgainst()
	{
		var settings = ConnectionSettingsFactory.Create();

		await using var dataSource = new ArchiveDataSource(settings);

		var states = await TickAsync(NewPoll(dataSource), ConsecutiveFailuresBeforeFault);

		var fault = Assert.IsType<ArchiveError>(states[^1]?.Fault);

		Assert.Equal(settings.Host, fault.Host);
		Assert.Equal(settings.Port, fault.Port);
		Assert.Equal(settings.Database, fault.Database);
	}

	// The flag, not the counter, is what stops the second banner: a run of failures is one fault, and the
	// operator is told once until a success has reset it. The number the one fault carries is therefore the
	// threshold that raised it and never the length of the run that followed — five failures here, three in
	// the report.
	[Fact]
	public async Task AFourthAndFifthConsecutiveFailureRaiseNothingFurther()
	{
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var states = await TickAsync(NewPoll(dataSource), ConsecutiveFailuresBeforeFault + 2);

		Assert.All(states.Take(ConsecutiveFailuresBeforeFault - 1), Assert.Null);

		var fault = Assert.IsType<ArchiveError>(states[ConsecutiveFailuresBeforeFault - 1]?.Fault);

		Assert.Equal($"{ConsecutiveFailuresBeforeFault}", fault.Detail);
		Assert.All(states.Skip(ConsecutiveFailuresBeforeFault), Assert.Null);
	}

	// The Rx wrapper subscribes a consumer that carries no error handler, so an escaping exception would
	// go unhandled on the UI scheduler rather than reaching anyone who could report it.
	[Fact]
	public async Task NoExceptionEscapesAFailingTick()
	{
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var poll = NewPoll(dataSource);

		var failures = await Record.ExceptionAsync(
			() => TickAsync(poll, ConsecutiveFailuresBeforeFault + 2));

		Assert.Null(failures);
	}

	// A failing tick never establishes a baseline, so the poll stays on the baseline branch and the next
	// tick has nothing to bind @lastSeen to.
	[Fact]
	public async Task AFailingTickLeavesTheBaselineUnread()
	{
		await using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());

		var poll = NewPoll(dataSource);

		await TickAsync(poll, ConsecutiveFailuresBeforeFault);

		Assert.Null(poll.LastSeen);
	}

	// A tick runs every poll interval, so it must not inherit ArchiveDataSource's five-minute backstop: a
	// server that accepts connections and then answers nothing would hold each tick for minutes and reach
	// the fault threshold only after fifteen of them, with a frozen chart and no banner in between.
	// Restated as literals rather than read off the two types, the way the failure threshold above is.
	[Theory]
	[InlineData(ArchiveStatements.RealtimeBaseline)]
	[InlineData(ArchiveStatements.RealtimePoll)]
	public void ATicksStatementCarriesItsOwnBoundAndNotTheDataSourceBackstop(string statementText)
	{
		const int tickBoundSeconds = 10;
		const int dataSourceBackstopSeconds = 300;

		using var dataSource = new ArchiveDataSource(ConnectionSettingsFactory.Create());
		using var connection = new NpgsqlConnection();

		using var command = RealtimePoll.CreateTickCommand(dataSource, statementText, connection);

		Assert.Equal(tickBoundSeconds, command.CommandTimeout);
		Assert.True(command.CommandTimeout < dataSourceBackstopSeconds);
	}

	[Fact]
	public void TheEngineRejectsANullIdentifierList()
	{
		var settings = ConnectionSettingsFactory.Create();

		using var dataSource = new ArchiveDataSource(settings);

		Assert.Throws<ArgumentNullException>(() => new RealtimePoll(
			dataSource,
			new ArchiveTimeConverter(settings.SourceTimeZone),
			new ArchiveExceptionMapper(settings),
			settings,
			null!,
			NullLogger.Instance));
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

	private static RealtimePoll NewPoll(ArchiveDataSource dataSource)
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
