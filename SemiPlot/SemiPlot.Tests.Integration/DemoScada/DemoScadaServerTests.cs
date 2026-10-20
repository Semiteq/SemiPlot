using System.Threading.Channels;

using AwesomeAssertions;

using Opc.Ua;

using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

using static SemiPlot.Tests.Integration.DemoScada.SimpleScadaNodeIds;

namespace SemiPlot.Tests.Integration.DemoScada;

[Collection(DemoScadaCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class DemoScadaServerTests : IClassFixture<DemoScadaServerFixture>
{
	private const int TestTimeoutMilliseconds = 120000;
	private const int UnknownPenId = 99999;
	private const string WatchlistGroup = "Watchlist";

	private static readonly TimeSpan _observationWindow = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan _clockTolerance = TimeSpan.FromSeconds(60);

	private readonly DemoScadaServerFixture _fixture;

	public DemoScadaServerTests(DemoScadaServerFixture fixture)
	{
		_fixture = fixture;
		_fixture.Host.Logs.Clear();
	}

	private static IReadOnlyList<SyntheticPen> Pens =>
		RawLayerGenerator.SelectPens(DemoScadaServerFixture.Options.PenCount);

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Objects_ListsOneRootAheadOfTheGroupFoldersNumberedFromThree()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var client = await OpcTestClient.ConnectAsync(_fixture.Server.EndpointUrl, cancellationToken);

		var folders = (await client.BrowseAsync(ObjectIds.ObjectsFolder, cancellationToken))
			.Where(reference => reference.BrowseName.NamespaceIndex == NamespaceIndex)
			.Select(reference => (
				Node: client.ToNodeId(reference.NodeId),
				Name: reference.BrowseName.Name ?? string.Empty))
			.ToArray();
		var groups = Pens
			.Select(pen => pen.PrimaryGroup)
			.Where(group => group.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		var expected = groups
			.Select((group, position) => (Node: FolderNode(group, FirstGroupFolderNumber + position), Name: group))
			.Prepend((Node: FolderNode(RootFolderName, RootFolderNumber), Name: RootFolderName));

		folders.Should().Equal(expected, _fixture.Host.ServerLog());
		folders.Should().ContainSingle(folder => folder.Name == RootFolderName, _fixture.Host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Root_OrganizesEveryPenUnderItsIdWithItsCatalogueNameAsADouble()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var client = await OpcTestClient.ConnectAsync(_fixture.Server.EndpointUrl, cancellationToken);

		var variables = await BrowseVariablesAsync(
			client,
			FolderNode(RootFolderName, RootFolderNumber),
			cancellationToken);

		variables.Should().BeEquivalentTo(
			Pens.Select(pen => (Node: PenNode(pen.PenId), pen.Name)),
			options => options.ComparingByValue<NodeId>(),
			_fixture.Host.ServerLog());

		foreach (var pen in Pens)
		{
			var node = await client.ReadNodeAsync(PenNode(pen.PenId), cancellationToken);

			node.Should().BeOfType<VariableNode>(_fixture.Host.ServerLog())
				.Which.DataType.Should().Be(DataTypeIds.Double, _fixture.Host.ServerLog());
		}
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task EachGroupFolder_OrganizesItsPrimaryGroupPensUnderTheSameIds()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var client = await OpcTestClient.ConnectAsync(_fixture.Server.EndpointUrl, cancellationToken);
		var groups = Pens
			.Where(pen => pen.PrimaryGroup.Length > 0)
			.GroupBy(pen => pen.PrimaryGroup, StringComparer.Ordinal)
			.ToArray();

		for (var position = 0; position < groups.Length; position++)
		{
			var group = groups[position];
			var folder = FolderNode(group.Key, FirstGroupFolderNumber + position);

			var variables = await BrowseVariablesAsync(client, folder, cancellationToken);

			variables.Should().BeEquivalentTo(
				group.Select(pen => (Node: PenNode(pen.PenId), pen.Name)),
				options => options.ComparingByValue<NodeId>(),
				"folder {0}; {1}",
				group.Key,
				_fixture.Host.ServerLog());
		}
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task TheUngroupedPenSitsUnderRootOnly_AndALaterGroupGetsNoFolder()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var client = await OpcTestClient.ConnectAsync(_fixture.Server.EndpointUrl, cancellationToken);
		var ungrouped = PenNode(SyntheticPenCatalog.UncommissionedPenId);

		var folders = (await client.BrowseAsync(ObjectIds.ObjectsFolder, cancellationToken))
			.Where(reference => reference.BrowseName.NamespaceIndex == NamespaceIndex)
			.ToArray();
		var holders = new List<string>();

		foreach (var folder in folders)
		{
			var variables = await BrowseVariablesAsync(client, client.ToNodeId(folder.NodeId), cancellationToken);

			if (variables.Any(variable => variable.Node == ungrouped))
			{
				holders.Add(folder.BrowseName.Name ?? string.Empty);
			}
		}

		Pens.Should().Contain(pen => pen.PenId == SyntheticPenCatalog.UncommissionedPenId);
		Pens.Should().Contain(pen => pen.Groups.Contains(WatchlistGroup));
		holders.Should().Equal([RootFolderName], _fixture.Host.ServerLog());
		folders.Should().NotContain(folder => folder.BrowseName.Name == WatchlistGroup, _fixture.Host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task EveryChangeCarriesTheLatticeValueAtItsTrueUtcInstant()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var options = DemoScadaServerFixture.Options;
		var converter = new ArchiveTimeConverter(DemoScadaHost.ArchiveZone);
		var intervalTicks = TimeSpan.FromSeconds(options.ChangeSeconds).Ticks;
		await using var client = await OpcTestClient.ConnectAsync(_fixture.Server.EndpointUrl, cancellationToken);
		var subscription = await client.SubscribeAsync(
			[.. Pens.Select(pen => PenNode(pen.PenId))],
			cancellationToken);

		await Task.Delay(_observationWindow, cancellationToken);
		var notifications = Drain(subscription.Notifications);

		foreach (var pen in Pens)
		{
			var received = notifications.Where(notification => notification.Node == PenNode(pen.PenId)).ToArray();
			var stamps = received.Select(notification => notification.Value.SourceTimestamp.ToDateTime()).ToArray();

			received.Should().HaveCountGreaterThanOrEqualTo(3, "pen {0}; {1}", pen.PenId, _fixture.Host.ServerLog());
			stamps.Should().BeInAscendingOrder("pen {0}; {1}", pen.PenId, _fixture.Host.ServerLog())
				.And.OnlyHaveUniqueItems("pen {0}; {1}", pen.PenId, _fixture.Host.ServerLog());

			foreach (var change in received.Skip(1))
			{
				var stamp = change.Value.SourceTimestamp.ToDateTime();
				var local = converter.ToArchiveLocal(stamp);
				var expected = LiveTailGenerator.ChangesAt(options, local).Single(row => row.Id == pen.PenId);

				(local.Ticks % intervalTicks).Should().Be(
					0,
					"pen {0} at {1:O} is no lattice instant; {2}",
					pen.PenId,
					local,
					_fixture.Host.ServerLog());
				change.Value.WrappedValue.GetDouble(double.NaN).Should().Be(
					expected.Value,
					"pen {0} at {1:O}; {2}",
					pen.PenId,
					local,
					_fixture.Host.ServerLog());
				stamp.Should().BeCloseTo(
					change.ReceivedUtc,
					_clockTolerance,
					"a local stamp sent as UTC misses by the zone's offset; {0}",
					_fixture.Host.ServerLog());
			}
		}
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AnUnknownNode_FailsItsMonitoredItemWhileThePensAreCreated()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var client = await OpcTestClient.ConnectAsync(_fixture.Server.EndpointUrl, cancellationToken);
		var unknown = PenNode(UnknownPenId);

		var subscription = await client.SubscribeAsync(
			[.. Pens.Select(pen => PenNode(pen.PenId)), unknown],
			cancellationToken);

		subscription.ItemStatuses[unknown].Should().Be(StatusCodes.BadNodeIdUnknown, _fixture.Host.ServerLog());
		subscription.ItemStatuses.Where(item => item.Key != unknown).Should().AllSatisfy(
			item => item.Value.Should().Be(StatusCodes.Good),
			_fixture.Host.ServerLog());
	}

	private static async Task<IReadOnlyList<(NodeId Node, string Name)>> BrowseVariablesAsync(
		OpcTestClient client,
		NodeId folder,
		CancellationToken cancellationToken)
	{
		var references = await client.BrowseAsync(folder, cancellationToken);

		return
		[
			.. references
				.Where(reference => reference.NodeClass == NodeClass.Variable)
				.Select(reference => (client.ToNodeId(reference.NodeId), reference.BrowseName.Name ?? string.Empty)),
		];
	}

	private static List<OpcNotification> Drain(ChannelReader<OpcNotification> notifications)
	{
		var drained = new List<OpcNotification>();

		while (notifications.TryRead(out var notification))
		{
			drained.Add(notification);
		}

		return drained;
	}
}

/// <summary>One demo server serving the stand's eight pens on a fast lattice, shared by a test class.</summary>
public sealed class DemoScadaServerFixture : IAsyncLifetime
{
	public static readonly FollowOptions Options = DemoScadaHost.Options with { ChangeSeconds = 0.25 };

	private DemoScadaServer? _server;

	public DemoScadaHost Host { get; } = new();

	public DemoScadaServer Server =>
		_server ?? throw new InvalidOperationException("The demo server was used before it was started.");

	public async ValueTask InitializeAsync()
	{
		_server = await Host.StartAsync(Options);
	}

	public async ValueTask DisposeAsync()
	{
		if (_server is not null)
		{
			await _server.DisposeAsync();
		}

		Host.Dispose();
	}
}
