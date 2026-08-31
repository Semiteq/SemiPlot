using FluentResults;

using SemiPlot.Core.Data.Errors;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads — in <see cref="ErrorWindow"/>, which
/// lays the three parts out as three blocks, and through <see cref="Describe"/> in the main window's
/// status row, which has one line. It is the only place a remedy is written: no consumer renders
/// <see cref="IError.Message"/> directly.
/// <para>
/// <c>StartupFailureMapperTests</c> enumerates <see cref="ArchiveFault"/> and
/// <see cref="ConnectionFileProblem"/> and fails when a member maps to <see cref="GenericTitle"/>.
/// </para>
/// </summary>
public static class StartupFailureMapper
{
	/// <summary>
	/// The title of the catch-all arm. The coverage test asserts no known error produces it.
	/// </summary>
	public const string GenericTitle = "Startup failed";

	/// <summary>
	/// One error as a single line: what happened, then what to do about it. The title is dropped rather
	/// than joined — it restates the detail's first clause.
	/// </summary>
	public static string Describe(IError error)
	{
		var view = Map(error);

		return $"{view.Detail} {view.Remedy}";
	}

	public static StartupFailureView Map(IError error)
	{
		ArgumentNullException.ThrowIfNull(error);

		return error switch
		{
			ConnectionFileError file => MapConnectionFile(file),
			ArchiveError archive => MapArchive(archive),
			StartupReadTimedOutError startupTimeout => MapStartupReadTimedOut(startupTimeout),
			IExceptionalError thrown => MapThrown(thrown),
			_ => MapUnknown(error)
		};
	}

	private static StartupFailureView MapConnectionFile(ConnectionFileError error)
	{
		if (error.Kind == ConnectionFileProblem.NotFound)
		{
			return new StartupFailureView(
				"Connection file not found",
				$"SemiPlot looked for the archive connection file at '{error.Path}' and found nothing there.",
				"Create that file, or start SemiPlot with --config-dir naming the directory that holds it.");
		}

		return new StartupFailureView(
			"Connection file cannot be read",
			$"The connection file '{error.Path}' exists but was rejected: {error.Reason}",
			error.Kind switch
			{
				ConnectionFileProblem.Unreadable =>
					"Give the account running SemiPlot read access to the file and close whatever holds it open.",
				ConnectionFileProblem.Unparseable =>
					"Repair the YAML syntax of the file; the reason above names the position that failed.",
				ConnectionFileProblem.MissingField =>
					"Add the field the reason names to the file. Every field of the format is required.",
				ConnectionFileProblem.OutOfRange =>
					"Correct the value the reason names to one inside the range it states.",
				ConnectionFileProblem.UnknownTimeZone =>
					"Set the time zone to an IANA identifier this machine knows, for example 'Europe/Moscow'.",
				_ => "Correct the file at the path above; the reason names what was rejected."
			});
	}

	private static StartupFailureView MapArchive(ArchiveError error)
	{
		var address = FormattableString.Invariant($"{error.Host}:{error.Port}");
		var archive = $"'{error.Database}' at {address}";

		return error.Kind switch
		{
			ArchiveFault.Unreachable => new StartupFailureView(
				"No connection to the archive",
				$"SemiPlot could not open a connection to {archive}.",
				"Check that the PostgreSQL server is running and that the host and port in the connection "
				+ "file are reachable from this machine — route, firewall and the server's listen address."),

			// The detail stops at what the answer proves: 28P01 and 28000 are raised while authenticating,
			// before PostgreSQL looks at the database name, so the archive was never confirmed to exist.
			ArchiveFault.AccessDenied => new StartupFailureView(
				"The archive refused the credentials",
				$"The server at {address} answered, but refused user '{error.Detail}' on '{error.Database}'.",
				"Correct the user name and password in the connection file, or grant that role SELECT on "
				+ "the archive tables. The network is not the problem — leave the host and port alone."),

			// A wrong database name reaches the server and looks the same as an unprovisioned one.
			ArchiveFault.DatabaseMissing => new StartupFailureView(
				"The archive is not provisioned",
				$"The server at {address} answers, but holds no database '{error.Database}'.",
				"Run 'semibase site' against this server to provision the database, or correct the "
				+ "database name in the connection file."),

			// One provisioning run creates every table SemiPlot reads, so the remedy never depends on which
			// table is absent.
			ArchiveFault.TableMissing => new StartupFailureView(
				"The archive is not provisioned",
				$"The archive {archive} holds no table '{error.Detail}'.",
				$"Table '{error.Detail}' is created by provisioning. Run 'semibase site' against this "
				+ "database to finish provisioning it."),

			// A lost live edge never opens the error window: it is drawn as a banner over a chart that keeps
			// its history, so the words say what is still true as well as what failed.
			ArchiveFault.ConnectionLost => new StartupFailureView(
				"The archive stopped answering",
				$"The live edge of {archive} stopped answering after {error.Detail} consecutive failed reads. "
				+ "The history already drawn is unaffected.",
				"Check that the PostgreSQL server is still running and still reachable from this machine. "
				+ "SemiPlot keeps polling and clears this by itself once the archive answers again."),

			// Nothing here holds the table's expected shape, so the detail quotes the server.
			ArchiveFault.ShapeUnexpected => new StartupFailureView(
				"The archive has an unexpected shape",
				$"The archive {archive} holds the tables SemiPlot reads, but not the columns they are expected "
				+ $"to carry. The server answered: {error.Detail}",
				"Table 'public.trends' and its columns are created by provisioning. Run 'semibase site' against "
				+ "this database to bring it to the shape this build reads, and check that nothing else has "
				+ "altered the table since."),

			ArchiveFault.QueryTimedOut => new StartupFailureView(
				"The archive ended the read",
				$"The read of {archive} was ended by the server (SQLSTATE 57014).",
				"Check statement_timeout for the reader role on the server and raise it, or narrow the window "
				+ "SemiPlot opens on; if the bound is not the cause, check whether an administrator cancelled the "
				+ "read."),

			_ => MapReadFailed(error, archive)
		};
	}

	private static StartupFailureView MapReadFailed(ArchiveError error, string archive)
	{
		var named = error.Detail.Length > 0;

		return new StartupFailureView(
			"The archive rejected the read",
			named
				? $"The archive {archive} rejected the read (SQLSTATE {error.Detail})."
				: $"The read of {archive} failed before the server answered, so it carries no SQLSTATE.",
			named
				? $"This build has no named handling for SQLSTATE {error.Detail}. Find the matching entry "
				  + "in the PostgreSQL server log and report it with the SemiPlot log file."
				: "The failure came from the client side. Report the SemiPlot log file, which carries the "
				  + "exception this build could not name.");
	}

	private static StartupFailureView MapStartupReadTimedOut(StartupReadTimedOutError error)
	{
		var bound = FormattableString.Invariant($"{error.Bound.TotalSeconds} s");

		return new StartupFailureView(
			"The archive did not answer in time",
			$"The startup read of the {error.Read} did not answer within {bound}. SemiPlot stopped "
			+ "waiting; the query is still running on the server.",
			"The connection was accepted, so the host and port are right. Check whether the server is "
			+ "overloaded and whether the archive is indexed on its time column.");
	}

	// A startup step that throws instead of failing — building the data source, or a read cancelled under
	// the provider — carries its exception here, and naming the type is what turns a silent exit into
	// something an operator can report.
	private static StartupFailureView MapThrown(IExceptionalError error)
	{
		return new StartupFailureView(
			"Startup failed unexpectedly",
			$"The startup sequence ended with {error.Exception.GetType().Name}: {error.Exception.Message}",
			"This build has no named handling for this failure. Report the SemiPlot log file, which "
			+ "carries the entry and its stack trace.");
	}

	private static StartupFailureView MapUnknown(IError error)
	{
		return new StartupFailureView(
			GenericTitle,
			error.Message,
			"This build has no named handling for this failure. Report the SemiPlot log file, which "
			+ "carries the full entry.");
	}
}
