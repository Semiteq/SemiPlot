using Opc.Ua;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// Picks the server's endpoint without security that admits an anonymous user, at the configured address.
/// </summary>
internal static class EndpointChoice
{
	/// <summary>
	/// A copy of the advertised endpoint with the server's token policies and the configured URL, so no connect
	/// dials the host name the server advertises.
	/// </summary>
	public static EndpointDescription? Pick(IEnumerable<EndpointDescription> endpoints, string configuredUrl)
	{
		var unsecured = endpoints.FirstOrDefault(endpoint => endpoint.SecurityMode == MessageSecurityMode.None
			&& endpoint.SecurityPolicyUri == SecurityPolicies.None
			&& AdmitsAnonymous(endpoint));

		if (unsecured is null)
		{
			return null;
		}

		var picked = (EndpointDescription)unsecured.Clone();
		picked.EndpointUrl = configuredUrl;

		return picked;
	}

	private static bool AdmitsAnonymous(EndpointDescription endpoint)
	{
		foreach (var token in endpoint.UserIdentityTokens)
		{
			if (token.TokenType == UserTokenType.Anonymous)
			{
				return true;
			}
		}

		return false;
	}
}
