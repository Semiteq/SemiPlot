using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The server answers, but something the provider needs is absent: the database itself (SQLSTATE
/// <c>3D000</c>) or a table inside an otherwise present database (SQLSTATE <c>42P01</c>). The remedy
/// follows the absent object rather than the state, so the consumer routes on
/// <see cref="MissingObject"/> and, on the table case, on <see cref="Table"/>:
/// <list type="table">
/// <item>
/// <term><see cref="ArchiveObject.Database"/></term>
/// <description><see cref="Table"/> is null and <c>semibase create</c> has not been run.</description>
/// </item>
/// <item>
/// <term><see cref="ArchiveObject.Table"/>, <c>trends</c></term>
/// <description>The table is the SCADA's: it has never run against this database.</description>
/// </item>
/// <item>
/// <term><see cref="ArchiveObject.Table"/>, <c>semiplot_tags</c></term>
/// <description>The table is SemiBase's: <c>semibase create</c> has not been run.</description>
/// </item>
/// </list>
/// </summary>
public sealed class ArchiveNotInitialisedError(
	string host,
	int port,
	string database,
	ArchiveObject missingObject,
	string? table)
	: Error(Describe(host, port, database, missingObject, table))
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public ArchiveObject MissingObject { get; } = missingObject;

	/// <summary>
	/// The absent table, or null when <see cref="MissingObject"/> is
	/// <see cref="ArchiveObject.Database"/> — <c>3D000</c> names no relation. On the
	/// <see cref="ArchiveObject.Table"/> case it is never null or blank: construction rejects that pair.
	/// </summary>
	public string? Table { get; } = table;

	private static string Describe(
		string host,
		int port,
		string database,
		ArchiveObject missingObject,
		string? table)
	{
		if (missingObject == ArchiveObject.Database)
		{
			return $"The server at {host}:{port} answers but holds no database '{database}'.";
		}

		// The table case always names one, which is what the consumer routes on. Enforced here, in the
		// base initialiser, so no instance contradicting the contract can exist to be rendered as
		// "holds no table ''".
		ArgumentException.ThrowIfNullOrWhiteSpace(table);

		return $"The archive '{database}' at {host}:{port} holds no table '{table}'.";
	}
}
