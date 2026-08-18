using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// A table the provider needs is absent from an otherwise present database (SQLSTATE <c>42P01</c>).
/// The remedy follows the table rather than the state, so the consumer routes on <see cref="Table"/>:
/// <c>trends</c> is the SCADA's and means it has never run against this database, while
/// <c>semiplot_tags</c> is SemiBase's and means <c>semibase create</c> has not been run.
/// </summary>
public sealed class ArchiveNotInitialisedError(string host, int port, string database, string table)
	: Error($"The archive '{database}' at {host}:{port} holds no table '{table}'.")
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public string Table { get; } = table;
}
