using System.Globalization;

using Npgsql;

namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// What the code wants, as opposed to what the file looks like.
/// </summary>
public sealed record PostgresConnectionSettings(
	string Host,
	int Port,
	string Database,
	string Username,
	string Password,
	TimeZoneInfo SourceTimeZone,
	TimeSpan PollInterval,
	string Schema)
{
	/// <summary>
	/// How long a connect attempt may take, in seconds. A caller's read bound must exceed it.
	/// </summary>
	public const int ConnectTimeoutSeconds = 15;

	/// <summary>
	/// Client backstop for a server that stops answering; the poll overrides it per tick.
	/// </summary>
	public const int CommandTimeoutSeconds = 300;

	public string ConnectionString
	{
		get
		{
			var builder = new NpgsqlConnectionStringBuilder
			{
				Host = Host,
				Port = Port,
				Database = Database,
				Username = Username,
				Password = Password,
				SearchPath = Schema,
				CommandTimeout = CommandTimeoutSeconds,
				Timeout = ConnectTimeoutSeconds
			};

			return builder.ConnectionString;
		}
	}

	/// <summary>
	/// Overridden so the password never reaches a log line.
	/// </summary>
	public override string ToString()
	{
		var invariant = CultureInfo.InvariantCulture;

		return $"{nameof(PostgresConnectionSettings)} {{ {nameof(Host)} = {Host}, "
			+ $"{nameof(Port)} = {Port.ToString(invariant)}, "
			+ $"{nameof(Database)} = {Database}, {nameof(Username)} = {Username}, {nameof(Password)} = ***, "
			+ $"{nameof(SourceTimeZone)} = {SourceTimeZone.Id}, {nameof(PollInterval)} = {PollInterval}, "
			+ $"{nameof(Schema)} = {Schema} }}";
	}
}
