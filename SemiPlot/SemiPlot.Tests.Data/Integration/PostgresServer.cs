using Npgsql;

namespace SemiPlot.Tests.Data.Integration;

// Credentials are BenchNames' fixed dummies, never parameters.
public sealed record PostgresServer(string Host, int Port)
{
	public string AdminConnectionString => AdminConnectionStringFor(BenchNames.MaintenanceDatabase);

	public string AdminConnectionStringFor(string database)
	{
		return ConnectionStringFor(
			database,
			BenchNames.SuperuserName,
			BenchNames.SuperuserPassword);
	}

	public string WriterConnectionStringFor(string database)
	{
		return ConnectionStringFor(
			database,
			BenchNames.WriterRole,
			BenchNames.WriterPassword);
	}

	public string ReaderConnectionStringFor(string database)
	{
		return ConnectionStringFor(
			database,
			BenchNames.ReaderRole,
			BenchNames.ReaderPassword);
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
