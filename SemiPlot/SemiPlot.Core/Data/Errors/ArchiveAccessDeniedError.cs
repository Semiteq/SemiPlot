using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// The server answers and the database exists, but the credentials are rejected (SQLSTATE <c>28P01</c>
/// or <c>28000</c>) or the role lacks a grant the read needs (SQLSTATE <c>42501</c>). The remedy is the
/// connection file's user, password or the role's grants — not the network, which is what
/// <see cref="ArchiveUnreachableError"/> sends the operator to.
/// </summary>
public sealed class ArchiveAccessDeniedError(string host, int port, string database, string username)
	: Error($"The archive '{database}' at {host}:{port} refused user '{username}'; check the password and the grants.")
{
	public string Host { get; } = host;

	public int Port { get; } = port;

	public string Database { get; } = database;

	public string Username { get; } = username;
}
