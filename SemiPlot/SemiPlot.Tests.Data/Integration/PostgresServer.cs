using Npgsql;

namespace SemiPlot.Tests.Data.Integration;

// The running server the gated tests talk to, whichever path produced it — a started container or the
// server SEMIPLOT_TEST_PG names. Host and port are kept apart from the connection string because
// semibase takes them as separate flags.
public sealed record PostgresServer(
	string SemibaseExecutable,
	string Host,
	int Port,
	string Superuser,
	string SuperuserPassword,
	string MaintenanceDatabase,
	string WriterPassword,
	string ReaderPassword)
{
	public string AdminConnectionString => AdminConnectionStringFor(MaintenanceDatabase);

	public string AdminConnectionStringFor(string database)
	{
		return ConnectionStringFor(database, Superuser, SuperuserPassword);
	}

	public string WriterConnectionStringFor(string database)
	{
		return ConnectionStringFor(database, SemibaseProvisioner.WriterRole, WriterPassword);
	}

	public string ReaderConnectionStringFor(string database)
	{
		return ConnectionStringFor(database, SemibaseProvisioner.ReaderRole, ReaderPassword);
	}

	private string ConnectionStringFor(string database, string user, string password)
	{
		var builder = new NpgsqlConnectionStringBuilder
		{
			Host = Host,
			Port = Port,
			Database = database,
			Username = user,
			Password = password
		};

		return builder.ConnectionString;
	}
}
