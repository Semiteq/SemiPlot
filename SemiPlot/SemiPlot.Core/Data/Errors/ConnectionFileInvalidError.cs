using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public sealed class ConnectionFileInvalidError(string path, ConnectionFileProblem kind, string reason)
	: Error($"The connection file '{path}' is invalid ({kind}): {reason}")
{
	public string Path { get; } = path;

	public ConnectionFileProblem Kind { get; } = kind;

	public string Reason { get; } = reason;
}
