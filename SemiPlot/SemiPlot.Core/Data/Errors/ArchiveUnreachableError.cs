using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public sealed class ArchiveUnreachableError(string host, int port, string database)
	: Error($"No connection to the archive '{database}' at {host}:{port}.")
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;
}
