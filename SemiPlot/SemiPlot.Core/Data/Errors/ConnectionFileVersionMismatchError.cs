using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public sealed class ConnectionFileVersionMismatchError(string path, string foundVersion, string expectedVersion)
	: Error($"The connection file '{path}' has version '{foundVersion}', expected '{expectedVersion}'.")
{
	public string Path { get; } = path;

	public string FoundVersion { get; } = foundVersion;

	public string ExpectedVersion { get; } = expectedVersion;
}
