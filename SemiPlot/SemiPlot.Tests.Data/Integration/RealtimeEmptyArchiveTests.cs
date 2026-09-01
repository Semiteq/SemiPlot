using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The branch every other gated class seeds its way past: an archive that holds public.trends and no row
// for the subscribed variables, which is what a commissioned installation looks like before the SCADA has
// written anything. The baseline scalar answers NULL there, and that answer must still arm the
// subscription.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeEmptyArchiveTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private static readonly int[] _subscribedPenIds = [1, 2];


	// One calendar day, so an appended row creates the single partition tp2026m01d01.
	private static readonly DateTime _day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	[Fact]
	public async Task TheBaselineOverAnArchiveWithNoRowStillArmsTheSubscription()
	{
		Fixture.RequireAvailable();

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var tick = await NewPoll(services).ReadOnceAsync(TestContext.Current.CancellationToken);

		Assert.Empty(tick.Samples);
		Assert.Same(ArchiveConnectionState.Connected, tick.StateChange);
	}

	// A NULL answer leaves lastSeen unset, so the next tick repeats the baseline read rather than binding
	// @lastSeen to nothing and going blind for good.
	[Fact]
	public async Task TheBaselineBranchRepeatsUntilTheArchiveCarriesARow()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);

		Assert.Null(poll.LastSeen);

		var second = await poll.ReadOnceAsync(cancellationToken);

		Assert.Null(poll.LastSeen);
		Assert.Empty(second.Samples);
		Assert.Null(second.StateChange);
	}

	// A tick that emitted the first row ever written would have to emit every row since the archive began.
	[Fact]
	public async Task TheFirstEverRowBecomesTheBaselineAndTheNextOneIsDelivered()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;
		var first = _day.AddMinutes(1);
		var second = _day.AddMinutes(2);

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(_subscribedPenIds[0], first, 1.5, cancellationToken);

		var baseline = await poll.ReadOnceAsync(cancellationToken);

		Assert.Empty(baseline.Samples);
		Assert.Equal(first, poll.LastSeen);

		await AppendAsync(_subscribedPenIds[1], second, 2.5, cancellationToken);

		var sample = Assert.Single((await poll.ReadOnceAsync(cancellationToken)).Samples);

		Assert.Equal(_subscribedPenIds[1], sample.PenId);
		Assert.Equal(_timeConverter.ToUtc(second), sample.TimestampUtc);
	}

	// The same branch through the composed provider, which is where a consumer meets it: the armed signal
	// has to reach ConnectionFaults over an archive that answers nothing.
	[Fact]
	public async Task ASubscriptionOverAnArchiveWithNoRowReportsConnected()
	{
		Fixture.RequireAvailable();

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();
		var armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var states = provider.ConnectionFaults.Subscribe(state =>
		{
			if (state.IsConnected)
			{
				armed.TrySetResult();
			}
		});

		using var subscription = provider.Subscribe(_subscribedPenIds).Subscribe(_ => { });

		await armed.Task;
	}

	private RealtimePoll NewPoll(IServiceProvider services)
	{
		return new RealtimePoll(
			services.GetRequiredService<NpgsqlDataSource>(),
			services.GetRequiredService<ArchiveTimeConverter>(),
			services.GetRequiredService<ArchiveExceptionMapper>(),
			services.GetRequiredService<PostgresConnectionSettings>(),
			_subscribedPenIds,
			NullLogger.Instance);
	}

	// Written as scada_writer, the role the SCADA itself writes with. The day partition is created first,
	// because an INSERT into a partitioned table with no matching partition lands in the default one.
	private async Task AppendAsync(
		int penId,
		DateTime archiveLocal,
		double value,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var partition = new NpgsqlCommand(
			PartitionScript.CreateStatement(archiveLocal.Date),
			connection);

		await partition.ExecuteNonQueryAsync(cancellationToken);

		await using var command = new NpgsqlCommand(
			"INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, 0, @t, @v, @q);",
			connection);

		command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = penId });
		command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Timestamp) { Value = archiveLocal });
		command.Parameters.Add(new NpgsqlParameter("v", NpgsqlDbType.Double) { Value = value });
		command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Integer)
		{
			Value = ArchiveRow.OrdinaryQuality
		});

		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}
