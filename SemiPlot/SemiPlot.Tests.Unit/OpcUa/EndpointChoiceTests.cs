using AwesomeAssertions;

using Opc.Ua;

using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

// The advertisements are the measured ones of the real Simple-Scada and of the stock 2.0.1 server; nothing is dialled.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class EndpointChoiceTests
{
	private const string ConfiguredUrl = "opc.tcp://127.0.0.1:4841";
	private const string SimpleScadaUrl = "opc.tcp://WORKING-PC:49875";
	private const string DemoUrl = "opc.tcp://127.0.0.1:4841";

	[Fact]
	public void TheRealScadasSevenEndpoints_YieldTheNoneOneAtTheConfiguredUrlWithItsAdvertisedToken()
	{
		EndpointDescription[] endpoints =
		[
			SimpleScada(MessageSecurityMode.Sign, SecurityPolicies.Basic128Rsa15),
			SimpleScada(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic128Rsa15),
			SimpleScada(MessageSecurityMode.None, SecurityPolicies.None),
			SimpleScada(MessageSecurityMode.Sign, SecurityPolicies.Basic256),
			SimpleScada(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256),
			SimpleScada(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256),
			SimpleScada(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256),
		];

		var chosen = EndpointChoice.Pick(endpoints, ConfiguredUrl);

		chosen.Should().NotBeNull();
		chosen!.SecurityMode.Should().Be(MessageSecurityMode.None);
		chosen.SecurityPolicyUri.Should().Be(SecurityPolicies.None);
		chosen.EndpointUrl.Should().Be(ConfiguredUrl);
		TokenOf(chosen).PolicyId.Should().Be("Anonymous");
		TokenOf(chosen).SecurityPolicyUri.Should().Be(SecurityPolicies.None);
	}

	[Fact]
	public void TheStockServersThreeEndpoints_YieldTheNoneOneWithItsOwnPolicyId()
	{
		EndpointDescription[] endpoints =
		[
			Endpoint(DemoUrl, MessageSecurityMode.None, SecurityPolicies.None, Anonymous("1", SecurityPolicies.None)),
			Endpoint(DemoUrl, MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256, Anonymous("2", "")),
			Endpoint(DemoUrl, MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256, Anonymous("2", "")),
		];

		var chosen = EndpointChoice.Pick(endpoints, ConfiguredUrl);

		chosen.Should().NotBeNull();
		chosen!.SecurityMode.Should().Be(MessageSecurityMode.None);
		TokenOf(chosen).PolicyId.Should().Be("1");
	}

	[Fact]
	public void ThePick_LeavesTheAdvertisedEndpointAsTheServerIssuedIt()
	{
		var advertised = SimpleScada(MessageSecurityMode.None, SecurityPolicies.None);

		var chosen = EndpointChoice.Pick([advertised], ConfiguredUrl);

		chosen.Should().NotBeSameAs(advertised);
		advertised.EndpointUrl.Should().Be(SimpleScadaUrl);
	}

	[Fact]
	public void NoNoneEndpoint_YieldsNothing()
	{
		EndpointDescription[] endpoints =
		[
			SimpleScada(MessageSecurityMode.Sign, SecurityPolicies.Basic256Sha256),
			SimpleScada(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256),
		];

		EndpointChoice.Pick(endpoints, ConfiguredUrl).Should().BeNull();
	}

	[Fact]
	public void ANoneEndpointWithoutAnAnonymousToken_YieldsNothing()
	{
		var userName = new UserTokenPolicy(UserTokenType.UserName)
		{
			PolicyId = "UserName",
			SecurityPolicyUri = SecurityPolicies.None,
		};
		EndpointDescription[] endpoints =
		[
			Endpoint(SimpleScadaUrl, MessageSecurityMode.None, SecurityPolicies.None, userName),
		];

		EndpointChoice.Pick(endpoints, ConfiguredUrl).Should().BeNull();
	}

	private static EndpointDescription SimpleScada(MessageSecurityMode mode, string policy)
	{
		return Endpoint(SimpleScadaUrl, mode, policy, Anonymous("Anonymous", SecurityPolicies.None));
	}

	private static UserTokenPolicy Anonymous(string policyId, string securityPolicyUri)
	{
		return new UserTokenPolicy(UserTokenType.Anonymous)
		{
			PolicyId = policyId,
			SecurityPolicyUri = securityPolicyUri,
		};
	}

	private static EndpointDescription Endpoint(
		string url,
		MessageSecurityMode mode,
		string policy,
		UserTokenPolicy token)
	{
		return new EndpointDescription
		{
			EndpointUrl = url,
			SecurityMode = mode,
			SecurityPolicyUri = policy,
			UserIdentityTokens = [token],
		};
	}

	private static UserTokenPolicy TokenOf(EndpointDescription endpoint)
	{
		endpoint.UserIdentityTokens.Count.Should().Be(1);

		return endpoint.UserIdentityTokens[0];
	}
}
