using System.Globalization;

using Npgsql;

namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// What the code wants, as opposed to what the file looks like. The source zone arrives already
/// resolved: the loader owns that resolution, because only it holds the file path an unknown
/// identifier has to be reported against.
/// </summary>
public sealed record PostgresConnectionSettings(
	string FileVersion,
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
	/// Built through <see cref="NpgsqlConnectionStringBuilder"/> rather than concatenated: a password
	/// holding ';' or '\'' survives the builder and corrupts a concatenated string silently, which then
	/// fails as an authentication error pointing at the wrong cause.
	/// <para><c>CommandTimeout = 0</c> is infinite: Npgsql's implicit 30 s would pre-empt the server's bound.</para>
	/// </summary>
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
				CommandTimeout = 0
			};

			return builder.ConnectionString;
		}
	}

	/// <summary>
	/// The compiler-generated form prints every member, which would put the password — and the
	/// connection string carrying it a second time — into any log line that formats the settings.
	/// </summary>
	public override string ToString()
	{
		var invariant = CultureInfo.InvariantCulture;

		return $"{nameof(PostgresConnectionSettings)} {{ {nameof(FileVersion)} = {FileVersion}, "
			+ $"{nameof(Host)} = {Host}, {nameof(Port)} = {Port.ToString(invariant)}, "
			+ $"{nameof(Database)} = {Database}, {nameof(Username)} = {Username}, {nameof(Password)} = ***, "
			+ $"{nameof(SourceTimeZone)} = {SourceTimeZone.Id}, {nameof(PollInterval)} = {PollInterval}, "
			+ $"{nameof(Schema)} = {Schema} }}";
	}
}
