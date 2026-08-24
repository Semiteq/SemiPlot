using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The server answers, but something the provider needs is absent: the database itself (SQLSTATE
/// <c>3D000</c>) or a table inside an otherwise present database (SQLSTATE <c>42P01</c>). One
/// provisioning run creates the database and every table SemiPlot reads, so the consumer routes on
/// <see cref="MissingObject"/> alone; <see cref="Table"/> names the absent object in the detail line
/// and carries no remedy of its own:
/// <list type="table">
/// <item>
/// <term><see cref="ArchiveObject.Database"/></term>
/// <description><see cref="Table"/> is null: the database itself has never been provisioned.</description>
/// </item>
/// <item>
/// <term><see cref="ArchiveObject.Table"/></term>
/// <description>The database exists but provisioning did not complete. Both <c>trends</c> and
/// <c>semiplot_tags</c> are SemiBase's and arrive in the same run.</description>
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

		// The table case always names one, which the detail line below interpolates. Enforced here, in
		// the base initialiser, so no instance contradicting the contract can exist to be rendered as
		// "holds no table ''".
		ArgumentException.ThrowIfNullOrWhiteSpace(table);

		return $"The archive '{database}' at {host}:{port} holds no table '{table}'.";
	}
}
