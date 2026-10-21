using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>What went wrong with the live source, as the operator needs it routed: one remedy per member.</summary>
public enum LiveSourceFault
{
	/// <summary>No session: the server does not answer, or the session it held was lost.</summary>
	Unreachable,

	/// <summary>The server answers but offers no endpoint without security and with anonymous access.</summary>
	EndpointRefused,

	/// <summary>The server answers but holds no project folder to read the pens from.</summary>
	ProjectMissing,

	/// <summary>The server does not know some pens, or serves a value that is not a number for them.</summary>
	PensUnserved
}

/// <summary>
/// Every failure the live source can report, keyed by <see cref="Kind"/>; consumers route on <see cref="Kind"/> and
/// never on <see cref="Error.Message"/>.
/// </summary>
public sealed class LiveSourceError(
	LiveSourceFault kind,
	string endpoint,
	IReadOnlyList<int>? unknownPenIds = null,
	IReadOnlyList<int>? notNumericPenIds = null)
	: Error(Describe(kind, endpoint, unknownPenIds ?? [], notNumericPenIds ?? []))
{
	public LiveSourceFault Kind { get; } = kind;

	public string Endpoint { get; } = endpoint;

	public IReadOnlyList<int> UnknownPenIds { get; } = unknownPenIds ?? [];

	public IReadOnlyList<int> NotNumericPenIds { get; } = notNumericPenIds ?? [];

	private static string Describe(
		LiveSourceFault kind,
		string endpoint,
		IReadOnlyList<int> unknownPenIds,
		IReadOnlyList<int> notNumericPenIds)
	{
		return kind switch
		{
			LiveSourceFault.Unreachable => $"No OPC UA session to {endpoint}.",
			LiveSourceFault.EndpointRefused =>
				$"The OPC UA server at {endpoint} offers no endpoint with security None and anonymous access.",
			LiveSourceFault.ProjectMissing => $"The OPC UA server at {endpoint} holds no project folder.",
			LiveSourceFault.PensUnserved => $"The OPC UA server at {endpoint} does not serve every pen."
				+ PenGroups(unknownPenIds, notNumericPenIds),
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
		};
	}

	private static string PenGroups(IReadOnlyList<int> unknownPenIds, IReadOnlyList<int> notNumericPenIds)
	{
		var unknown = unknownPenIds.Count > 0
			? FormattableString.Invariant($" Unknown pen ids: {string.Join(", ", unknownPenIds)}.")
			: "";
		var notNumeric = notNumericPenIds.Count > 0
			? FormattableString.Invariant($" Non-numeric pen ids: {string.Join(", ", notNumericPenIds)}.")
			: "";

		return unknown + notNumeric;
	}
}
