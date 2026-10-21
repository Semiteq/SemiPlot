using AwesomeAssertions;

using SemiPlot.Core.Configuration;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Configuration;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class NetworkAddressTests
{
	[Theory]
	[InlineData("0.0.0.0")]
	[InlineData("127.0.0.1")]
	[InlineData("192.168.0.10")]
	[InlineData("255.255.255.255")]
	public void AnIPv4AddressOfFourDecimalOctetsIsAHost(string host)
	{
		NetworkAddress.IsIPv4(host).Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("localhost")]
	[InlineData("::1")]
	[InlineData("::ffff:127.0.0.1")]
	[InlineData("127.1")]
	[InlineData("1.2.3.4.5")]
	[InlineData("1..2.3")]
	[InlineData("256.0.0.1")]
	[InlineData("1.2.3.1000")]
	[InlineData("010.0.0.1")]
	[InlineData("+1.2.3.4")]
	[InlineData(" 127.0.0.1")]
	[InlineData("\uFF11.2.3.4")]
	public void AnythingButFourDecimalOctetsIsNotAHost(string? host)
	{
		NetworkAddress.IsIPv4(host).Should().BeFalse();
	}
}
