using FluentResults;

using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.MainWindow;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads: three blocks through <see cref="Map"/>,
/// one line through <see cref="Describe"/>. The only place a remedy is written.
/// </summary>
public static class ArchiveFailureMapper
{
	private const string GenericTitle = "Startup failed";

	/// <summary>
	/// One error as a single line: what happened, then what to do about it.
	/// </summary>
	public static string Describe(IError error)
	{
		var view = Map(error);

		return $"{view.Detail} {view.Remedy}";
	}

	public static ArchiveFailureView Map(IError error)
	{
		return error switch
		{
			ConnectionFileError file => MapConnectionFile(file),
			ArchiveError archive => MapArchive(archive),
			StartupReadTimedOutError startupTimeout => MapStartupReadTimedOut(startupTimeout),
			IExceptionalError thrown => MapThrown(thrown),
			_ => MapUnknown(error)
		};
	}

	private static ArchiveFailureView MapConnectionFile(ConnectionFileError error)
	{
		if (error.Kind == ConnectionFileProblem.NotFound)
		{
			return new ArchiveFailureView(
				"Connection file not found",
				$"SemiPlot looked for the archive connection file at '{error.Path}' and found nothing there.",
				"Create that file, or start SemiPlot with --config-dir naming the directory that holds it.");
		}

		return new ArchiveFailureView(
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
					"Set the time zone to an identifier this machine knows: IANA, or the id 'tzutil /g' prints.",
				_ => "Correct the file at the path above; the reason names what was rejected."
			});
	}

	private static ArchiveFailureView MapArchive(ArchiveError error)
	{
		var address = FormattableString.Invariant($"{error.Host}:{error.Port}");
		var archive = $"'{error.Database}' at {address}";

		return error.Kind switch
		{
			ArchiveFault.Unreachable => new ArchiveFailureView(
				"No connection to the archive",
				$"SemiPlot could not open a connection to {archive}.",
				"Check that the PostgreSQL server is running and that the host and port in the connection "
				+ "file are reachable from this machine — route, firewall and the server's listen address."),

			// 28P01 and 28000 are raised before PostgreSQL looks at the database name, so the archive was
			// never confirmed to exist.
			ArchiveFault.AccessDenied => new ArchiveFailureView(
				"The archive refused the credentials",
				$"The server at {address} answered, but refused user '{error.Detail}' on '{error.Database}'.",
				"Correct the user name and password in the connection file, or grant that role SELECT on "
				+ "the archive tables. The network is not the problem — leave the host and port alone."),

			// A wrong database name reaches the server and looks the same as an unprovisioned one.
			ArchiveFault.DatabaseMissing => new ArchiveFailureView(
				"The archive is not provisioned",
				$"The server at {address} answers, but holds no database '{error.Database}'.",
				"Run 'semibase site' against this server to provision the database, or correct the "
				+ "database name in the connection file."),

			// One provisioning run creates every table SemiPlot reads, so the remedy never depends on which
			// table is absent.
			ArchiveFault.TableMissing => new ArchiveFailureView(
				"The archive is not provisioned",
				$"The archive {archive} holds no table '{error.Detail}'.",
				$"Table '{error.Detail}' is created by provisioning. Run 'semibase site' against this "
				+ "database to finish provisioning it."),

			// A lost live edge never opens the startup failure panel: it is drawn as a banner over a chart
			// that keeps its history, so the words say what is still true as well as what failed.
			ArchiveFault.ConnectionLost => new ArchiveFailureView(
				"The archive stopped answering",
				$"The live edge of {archive} stopped answering after {error.Detail} consecutive failed reads. "
				+ "The history already drawn is unaffected.",
				"Check that the PostgreSQL server is still running and still reachable from this machine. "
				+ "SemiPlot keeps polling and clears this by itself once the archive answers again."),

			ArchiveFault.ShapeUnexpected => new ArchiveFailureView(
				"The archive has an unexpected shape",
				$"The archive {archive} holds the tables SemiPlot reads, but not the columns they are expected "
				+ $"to carry. The server answered: {error.Detail}",
				"Table 'public.trends' and its columns are created by provisioning. Run 'semibase site' against "
				+ "this database to bring it to the shape this build reads, and check that nothing else has "
				+ "altered the table since."),

			ArchiveFault.QueryTimedOut => new ArchiveFailureView(
				"The archive ended the read",
				$"The read of {archive} was ended by the server (SQLSTATE 57014).",
				"Check statement_timeout for the reader role on the server and raise it, or narrow the window "
				+ "SemiPlot opens on; if the bound is not the cause, check whether an administrator cancelled the "
				+ "read."),

			_ => MapReadFailed(error, archive)
		};
	}

	private static ArchiveFailureView MapReadFailed(ArchiveError error, string archive)
	{
		var named = error.Detail.Length > 0;

		return new ArchiveFailureView(
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

	private static ArchiveFailureView MapStartupReadTimedOut(StartupReadTimedOutError error)
	{
		var bound = FormattableString.Invariant($"{error.Bound.TotalSeconds} s");

		return new ArchiveFailureView(
			"The archive did not answer in time",
			$"The startup read of the {error.Read} did not answer within {bound}. SemiPlot stopped "
			+ "waiting; the query is still running on the server.",
			"The connection was accepted, so the host and port are right. Check whether the server is "
			+ "overloaded and whether the archive is indexed on its time column.");
	}

	private static ArchiveFailureView MapThrown(IExceptionalError error)
	{
		return new ArchiveFailureView(
			"Startup failed unexpectedly",
			$"The startup sequence ended with {error.Exception.GetType().Name}: {error.Exception.Message}",
			"This build has no named handling for this failure. Report the SemiPlot log file, which "
			+ "carries the entry and its stack trace.");
	}

	private static ArchiveFailureView MapUnknown(IError error)
	{
		return new ArchiveFailureView(
			GenericTitle,
			error.Message,
			"This build has no named handling for this failure. Report the SemiPlot log file, which "
			+ "carries the full entry.");
	}
}
