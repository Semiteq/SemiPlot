using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public enum ConnectionFileProblem
{
	Unparseable,
	MissingField,
	OutOfRange,
	UnknownTimeZone
}

/// <summary>
/// The connection section could not be turned into settings. <see cref="Kind"/> is what the operator's
/// remedy routes on; <see cref="Reason"/> names the field or the position, never the section's own values.
/// </summary>
public sealed class ConnectionFileError(string path, ConnectionFileProblem kind, string reason = "")
	: Error($"The connection folder '{path}' is invalid ({kind}): {reason}")
{
	public string Path { get; } = path;

	public ConnectionFileProblem Kind { get; } = kind;

	public string Reason { get; } = reason;
}
