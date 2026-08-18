using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public sealed class ArchiveDatabaseMissingError(string host, int port, string database)
	: Error($"The server at {host}:{port} answers but holds no database '{database}'; run semibase create.")
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;
}
