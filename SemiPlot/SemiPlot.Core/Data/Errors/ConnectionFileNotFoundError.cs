using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public sealed class ConnectionFileNotFoundError(string path)
	: Error($"The archive connection file '{path}' does not exist.")
{
	public string Path { get; } = path;
}
