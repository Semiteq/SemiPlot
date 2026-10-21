using System.Globalization;

namespace SemiPlot.Core.Configuration;

/// <summary>The host and port rules every connection section shares.</summary>
public static class NetworkAddress
{
	public const int LowestPort = 1;

	public const int HighestPort = 65535;

	private const int HighestOctet = 255;

	/// <summary>
	/// Four dot-separated decimal octets from 0 to 255 with no leading zero, so no shortened or octal form.
	/// </summary>
	public static bool IsIPv4(string? text)
	{
		if (text is null)
		{
			return false;
		}

		var octets = text.Split('.');

		return octets.Length == 4 && octets.All(IsOctet);
	}

	private static bool IsOctet(string octet)
	{
		return octet.Length is >= 1 and <= 3
			&& octet.All(char.IsAsciiDigit)
			&& (octet.Length == 1 || octet[0] != '0')
			&& int.Parse(octet, CultureInfo.InvariantCulture) <= HighestOctet;
	}
}
