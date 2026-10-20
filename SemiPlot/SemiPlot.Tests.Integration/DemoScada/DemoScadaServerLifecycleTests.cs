using System.Net;
using System.Net.Sockets;

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
public sealed class DemoScadaServerLifecycleTests : IDisposable
{
	private const int TestTimeoutMilliseconds = 120000;
	private const string NoneSecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#None";
	private const string Basic256Sha256SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";
	private const double OneChangePerDaySeconds = 86400;
	private const double ChangeSecondsPastTheTaskDelayLimit = 100000.0 * 86400;

	private static readonly TimeSpan _changeFreeBudget = TimeSpan.FromSeconds(30);

	private readonly DemoScadaHost _host = new();

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task TheServerAdvertisesNoneSignAndSignAndEncrypt_EachWithOneAnonymousToken()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var server = await _host.StartAsync(DemoScadaHost.Options);

		var endpoints = await OpcTestClient.GetEndpointsAsync(server.EndpointUrl, cancellationToken);

		endpoints.Select(endpoint => (endpoint.SecurityMode, endpoint.SecurityPolicyUri)).Should().BeEquivalentTo(
			[
				(MessageSecurityMode.None, NoneSecurityPolicyUri),
				(MessageSecurityMode.Sign, Basic256Sha256SecurityPolicyUri),
				(MessageSecurityMode.SignAndEncrypt, Basic256Sha256SecurityPolicyUri),
			],
			_host.ServerLog());

		endpoints.Should().AllSatisfy(
			endpoint => endpoint.UserIdentityTokens.ToArray().Should().ContainSingle()
				.Which.TokenType.Should().Be(UserTokenType.Anonymous),
			_host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task TheNoneEndpointAdvertisesItsAnonymousTokenOnTheNonePolicy()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var server = await _host.StartAsync(DemoScadaHost.Options);

		var endpoints = await OpcTestClient.GetEndpointsAsync(server.EndpointUrl, cancellationToken);

		endpoints.Single(endpoint => endpoint.SecurityMode == MessageSecurityMode.None)
			.UserIdentityTokens.ToArray().Should().ContainSingle()
			.Which.SecurityPolicyUri.Should().Be(
				NoneSecurityPolicyUri,
				"the real Simple-Scada advertises its anonymous token on #None; {0}",
				_host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ASessionOpensOverNoneWithTheAdvertisedAnonymousToken()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var server = await _host.StartAsync(DemoScadaHost.Options);

		await using var client = await OpcTestClient.ConnectAsync(server.EndpointUrl, cancellationToken);

		client.Connected.Should().BeTrue(_host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task TheSimpleScadaNamespaceSitsAtIndexThree()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var server = await _host.StartAsync(DemoScadaHost.Options);

		await using var client = await OpcTestClient.ConnectAsync(server.EndpointUrl, cancellationToken);

		client.NamespaceUris.GetIndex(NamespaceUri).Should().Be(NamespaceIndex, _host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task APortHeldOnLoopback_FailsTheStartNamingThePort()
	{
		using var holder = new TcpListener(IPAddress.Loopback, DemoScadaHost.FreePort());
		holder.Start();
		var port = ((IPEndPoint)holder.LocalEndpoint).Port;

		var start = () => _host.StartOnPortAsync(DemoScadaHost.Options with { OpcPort = port });

		(await start.Should().ThrowAsync<SeederException>(_host.ServerLog()))
			.WithMessage($"*OPC UA port {port} is in use*");
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AServerStoppedWithASessionAttached_StartsAgainOnItsPort()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		await using var first = await _host.StartAsync(DemoScadaHost.Options);
		var port = new Uri(first.EndpointUrl).Port;
		await using var abandoned = await OpcTestClient.ConnectAsync(first.EndpointUrl, cancellationToken);

		await first.DisposeAsync();
		await using var second = await _host.StartOnPortAsync(DemoScadaHost.Options with { OpcPort = port });
		await using var client = await OpcTestClient.ConnectAsync(second.EndpointUrl, cancellationToken);

		second.EndpointUrl.Should().Be(first.EndpointUrl);
		client.Connected.Should().BeTrue(_host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ASecondStartInTheSameCertificateDirectory_ReusesTheCertificate()
	{
		var certificates = Path.Combine(_host.CertificateDirectory, "own", "certs");
		await using (await _host.StartAsync(DemoScadaHost.Options))
		{
		}

		var firstCertificate = Directory.GetFiles(certificates).Should().ContainSingle(_host.ServerLog()).Which;
		var firstBytes = await File.ReadAllBytesAsync(firstCertificate, TestContext.Current.CancellationToken);

		await using var second = await _host.StartAsync(DemoScadaHost.Options);

		var secondCertificate = Directory.GetFiles(certificates).Should().ContainSingle(_host.ServerLog()).Which;
		secondCertificate.Should().Be(firstCertificate, _host.ServerLog());
		(await File.ReadAllBytesAsync(secondCertificate, TestContext.Current.CancellationToken))
			.Should().Equal(firstBytes, _host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Completion_RunsUntilDisposeAsync_AndThenCompletesSuccessfully()
	{
		await using var server = await _host.StartAsync(DemoScadaHost.Options);

		server.Completion.IsCompleted.Should().BeFalse(_host.ServerLog());

		await server.Invoking(started => started.DisposeAsync().AsTask()).Should().NotThrowAsync(_host.ServerLog());
		server.Completion.IsCompletedSuccessfully.Should().BeTrue(_host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AFaultedPublishingLoop_FaultsCompletion_AndDisposeAsyncRethrowsTheFault()
	{
		await using var server = await _host.StartAsync(
			DemoScadaHost.Options with { ChangeSeconds = ChangeSecondsPastTheTaskDelayLimit });

		var loopFault = (await server.Awaiting(started => started.Completion)
			.Should().ThrowAsync<Exception>(_host.ServerLog())).Which;

		(await server.Invoking(started => started.DisposeAsync().AsTask())
			.Should().ThrowAsync<Exception>(_host.ServerLog())).Which.Should().BeSameAs(loopFault, _host.ServerLog());
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task EveryFirstNotificationAfterAStart_CarriesTheStartInstantAndTheLatticeValueThere()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var options = DemoScadaHost.Options with { ChangeSeconds = OneChangePerDaySeconds };
		var converter = new ArchiveTimeConverter(DemoScadaHost.ArchiveZone);
		var pens = RawLayerGenerator.SelectPens(options.PenCount);
		var nextChangeUtc = await WaitUntilNoChangeIsNearAsync(options, converter, cancellationToken);

		var beforeStart = DateTime.UtcNow;
		await using var server = await _host.StartAsync(options);
		var afterStart = DateTime.UtcNow;
		await using var client = await OpcTestClient.ConnectAsync(server.EndpointUrl, cancellationToken);
		var subscription = await client.SubscribeAsync(
			[.. pens.Select(pen => PenNode(pen.PenId))],
			cancellationToken);
		var firsts = new Dictionary<NodeId, OpcNotification>();

		await foreach (var notification in subscription.Notifications.ReadAllAsync(cancellationToken))
		{
			firsts.TryAdd(notification.Node, notification);

			if (firsts.Count == pens.Count)
			{
				break;
			}
		}

		DateTime.UtcNow.Should().BeBefore(nextChangeUtc, "the test window crossed a lattice change; raise the budget");

		foreach (var pen in pens)
		{
			var first = firsts[PenNode(pen.PenId)].Value;
			var stamp = first.SourceTimestamp.ToDateTime();
			var expected = LiveTailGenerator.ChangesAt(options, converter.ToArchiveLocal(stamp))
				.Single(row => row.Id == pen.PenId);

			stamp.Should().BeOnOrAfter(beforeStart, "pen {0}; {1}", pen.PenId, _host.ServerLog())
				.And.BeOnOrBefore(afterStart, "pen {0}; {1}", pen.PenId, _host.ServerLog());
			first.WrappedValue.GetDouble(double.NaN).Should().Be(
				expected.Value,
				"pen {0}; {1}",
				pen.PenId,
				_host.ServerLog());
		}
	}

	public void Dispose()
	{
		_host.Dispose();
	}

	/// <summary>Waits past a lattice change due within the budget and returns the next change in UTC.</summary>
	private static async Task<DateTime> WaitUntilNoChangeIsNearAsync(
		FollowOptions options,
		ArchiveTimeConverter converter,
		CancellationToken cancellationToken)
	{
		var nextChangeUtc = NextChangeUtc(options, converter);
		var untilNextChange = nextChangeUtc - DateTime.UtcNow;

		if (untilNextChange >= _changeFreeBudget)
		{
			return nextChangeUtc;
		}

		await Task.Delay(untilNextChange + TimeSpan.FromSeconds(1), cancellationToken);

		return NextChangeUtc(options, converter);
	}

	private static DateTime NextChangeUtc(FollowOptions options, ArchiveTimeConverter converter)
	{
		var latestChange = LiveTailGenerator.ChangesAt(options, converter.ToArchiveLocal(DateTime.UtcNow))[0];

		return converter.ToUtc(latestChange.Timestamp.AddSeconds(options.ChangeSeconds));
	}
}
