using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Integration;

// Credentials are BenchRoles' fixed dummies, never parameters.
public sealed record PostgresServer(string Host, int Port)
{
	public string AdminConnectionString => AdminConnectionStringFor(BenchRoles.MaintenanceDatabase);

	public string AdminConnectionStringFor(string database)
	{
		return ConnectionStringFor(
			database,
			BenchRoles.SuperuserName,
			BenchRoles.SuperuserPassword);
	}

	public string WriterConnectionStringFor(string database)
	{
		return ConnectionStringFor(
			database,
			BenchRoles.WriterRole,
			BenchRoles.WriterPassword);
	}

	public string ReaderConnectionStringFor(string database)
	{
		return ConnectionStringFor(
			database,
			BenchRoles.ReaderRole,
			BenchRoles.ReaderPassword);
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
