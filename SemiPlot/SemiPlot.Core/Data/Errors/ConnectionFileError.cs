using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The connection file could not be turned into settings. <see cref="Kind"/> is what the operator's remedy
/// routes on; <see cref="Reason"/> names the field or the position, never the file's own values.
/// </summary>
public sealed class ConnectionFileError(string path, ConnectionFileProblem kind, string reason = "")
	: Error(Describe(path, kind, reason))
{
	public string Path { get; } = path;

	public ConnectionFileProblem Kind { get; } = kind;

	public string Reason { get; } = reason;

	private static string Describe(string path, ConnectionFileProblem kind, string reason)
	{
		return kind == ConnectionFileProblem.NotFound
			? $"The archive connection file '{path}' does not exist."
			: $"The connection file '{path}' is invalid ({kind}): {reason}";
	}
}
