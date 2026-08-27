using FluentResults;

using SemiPlot.Core.Data.Errors;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads — in <see cref="ErrorWindow"/>, which
/// lays the three parts out as three blocks, and through <see cref="Describe"/> in the main window's
/// status rows, which have one line each. One arm per public error type, each naming a remedy of its own;
/// the catch-all arm exists because the compiler demands one over an interface, not as a place for a type
/// to land.
/// <para>
/// It is the only place a remedy is written. An operator told a state alone — "the live edge stopped
/// answering" — is told nothing to do about it, so no consumer renders <see cref="IError.Message"/>
/// directly.
/// </para>
/// <para>
/// The gate against a missing arm is <c>StartupFailureMapperTests</c>, which enumerates the public
/// error types by reflection and fails when one maps to <see cref="GenericTitle"/>. It cannot be the
/// compiler: <c>CS8509</c> fires on any switch expression exhaustiveness cannot be proven for, and over
/// an interface it never can be, so a switch covering every type still warns and promoting that warning
/// to an error would stop the build.
/// </para>
/// </summary>
public static class StartupFailureMapper
{
	/// <summary>
	/// The title of the catch-all arm. The coverage test asserts no known error type produces it, which
	/// is what makes an unmapped type a failing test rather than a vague window.
	/// </summary>
	public const string GenericTitle = "Startup failed";

	/// <summary>
	/// One error as a single line: what happened, then what to do about it. This is what the main
	/// window's status rows render, where there is no room to lay <see cref="StartupFailureView"/> out
	/// in three parts. The title is dropped rather than joined — it restates the detail's first clause.
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
			ConnectionFileNotFoundError notFound => MapConnectionFileNotFound(notFound),
			ConnectionFileInvalidError invalid => MapConnectionFileInvalid(invalid),
			ArchiveUnreachableError unreachable => MapArchiveUnreachable(unreachable),
			ArchiveAccessDeniedError denied => MapArchiveAccessDenied(denied),
			ArchiveNotInitialisedError notInitialised => MapArchiveNotInitialised(notInitialised),
			ArchiveConnectionLostError connectionLost => MapArchiveConnectionLost(connectionLost),
			ArchiveShapeUnexpectedError shapeUnexpected => MapArchiveShapeUnexpected(shapeUnexpected),
			ArchiveDefaultPartitionNotEmptyError defaultPartition => MapArchiveDefaultPartitionNotEmpty(
				defaultPartition),
			ArchiveQueryTimedOutError serverTimeout => MapArchiveQueryTimedOut(serverTimeout),
			ArchiveReadFailedError readFailed => MapArchiveReadFailed(readFailed),
			StartupReadTimedOutError startupTimeout => MapStartupReadTimedOut(startupTimeout),
			IExceptionalError thrown => MapThrown(thrown),
			_ => MapUnknown(error)
		};
	}

	private static StartupFailureView MapConnectionFileNotFound(ConnectionFileNotFoundError error)
	{
		return new StartupFailureView(
			"Connection file not found",
			$"SemiPlot looked for the archive connection file at '{error.Path}' and found nothing there.",
			"Create that file, or start SemiPlot with --config-dir naming the directory that holds it.");
	}

	private static StartupFailureView MapConnectionFileInvalid(ConnectionFileInvalidError error)
	{
		return new StartupFailureView(
			"Connection file cannot be read",
			$"The connection file '{error.Path}' exists but was rejected: {error.Reason}",
			DescribeConnectionFileRemedy(error.Kind));
	}

	private static string DescribeConnectionFileRemedy(ConnectionFileProblem kind)
	{
		return kind switch
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
			ConnectionFileProblem.VersionMismatch =>
				"The file declares a format version this build does not read. Convert the file to the "
				+ "version the reason names, or install the SemiPlot build that matches the file.",
			_ => "Correct the file at the path above; the reason names what was rejected."
		};
	}

	private static StartupFailureView MapArchiveUnreachable(ArchiveUnreachableError error)
	{
		return new StartupFailureView(
			"No connection to the archive",
			FormattableString.Invariant(
				$"SemiPlot could not open a connection to '{error.Database}' at {error.Host}:{error.Port}."),
			"Check that the PostgreSQL server is running and that the host and port in the connection "
			+ "file are reachable from this machine — route, firewall and the server's listen address.");
	}

	private static StartupFailureView MapArchiveAccessDenied(ArchiveAccessDeniedError error)
	{
		// The detail stops at what the answer proves. Two of the three SQLSTATEs routed here — 28P01 and
		// 28000 — are raised while authenticating, before PostgreSQL looks at the database name, so the
		// archive was never confirmed to exist on this path.
		var server = FormattableString.Invariant($"The server at {error.Host}:{error.Port}");

		return new StartupFailureView(
			"The archive refused the credentials",
			$"{server} answered, but refused user '{error.Username}' on '{error.Database}'.",
			"Correct the user name and password in the connection file, or grant that role SELECT on "
			+ "the archive tables. The network is not the problem — leave the host and port alone.");
	}

	private static StartupFailureView MapArchiveNotInitialised(ArchiveNotInitialisedError error)
	{
		var detail = error.MissingObject == ArchiveObject.Database
			? FormattableString.Invariant(
				$"The server at {error.Host}:{error.Port} answers, but holds no database '{error.Database}'.")
			: FormattableString.Invariant(
				$"The archive '{error.Database}' at {error.Host}:{error.Port} holds no table '{error.Table}'.");

		return new StartupFailureView("The archive is not provisioned", detail, DescribeMissingObjectRemedy(error));
	}

	// The remedy follows the state, never the table name: one provisioning run creates the database and
	// every table SemiPlot reads, so both states end at the same command. The missing-database state adds
	// the connection file, because a wrong database name reaches the server and looks the same.
	private static string DescribeMissingObjectRemedy(ArchiveNotInitialisedError error)
	{
		if (error.MissingObject == ArchiveObject.Database)
		{
			return "Run 'semibase site' against this server to provision the database, or correct the "
				+ "database name in the connection file.";
		}

		return $"Table '{error.Table}' is created by provisioning. Run 'semibase site' against this "
			+ "database to finish provisioning it.";
	}

	// This one never opens the error window: a lost live edge arrives on the connection stream long after
	// startup and is drawn as a banner over a chart that keeps its history. The arm exists because the
	// mapper is the one place a public error type is turned into words, and the coverage test holds it there.
	private static StartupFailureView MapArchiveConnectionLost(ArchiveConnectionLostError error)
	{
		var edge = FormattableString.Invariant(
			$"The live edge of '{error.Database}' at {error.Host}:{error.Port}");
		var failures = FormattableString.Invariant($"{error.FailureThreshold} consecutive failed reads");

		var detail = $"{edge} stopped answering after {failures}. The history already drawn is unaffected.";

		return new StartupFailureView(
			"The archive stopped answering",
			detail,
			"Check that the PostgreSQL server is still running and still reachable from this machine. "
			+ "SemiPlot keeps polling and clears this by itself once the archive answers again.");
	}

	// The remedy names the provisioning that owns public.trends and stops there. Nothing here holds the
	// table's expected shape, so this build cannot say which column is wrong beyond what the server said.
	private static StartupFailureView MapArchiveShapeUnexpected(ArchiveShapeUnexpectedError error)
	{
		var archive = FormattableString.Invariant($"'{error.Database}' at {error.Host}:{error.Port}");

		var detail = $"The archive {archive} holds the tables SemiPlot reads, but not the columns they "
			+ $"are expected to carry. The server answered: {error.Detail}";

		return new StartupFailureView(
			"The archive has an unexpected shape",
			detail,
			"Table 'public.trends' and its columns are created by provisioning. Run 'semibase site' against "
			+ "this database to bring it to the shape this build reads, and check that nothing else has "
			+ "altered the table since.");
	}

	// Like the lost-connection arm, this one never opens the error window: the startup health check carries
	// it out beside a successful read and it is drawn as a banner over a working chart. The arm exists
	// because the mapper is the one place a public error type is turned into words, and the coverage test
	// enumerates the vocabulary by namespace rather than by which types can reach a window.
	private static StartupFailureView MapArchiveDefaultPartitionNotEmpty(ArchiveDefaultPartitionNotEmptyError error)
	{
		var archive = FormattableString.Invariant($"'{error.Database}' at {error.Host}:{error.Port}");

		var detail = $"The archive {archive} holds rows in '{error.Partition}', the partition that catches "
			+ "samples whose own day was never created. Those rows are still read, and every read that "
			+ "cannot skip that partition is slower for them.";

		return new StartupFailureView(
			"The archive's default partition holds rows",
			detail,
			"The rows were written by the SCADA, so the remedy is on that side: find out why the daily "
			+ "partition was missing at write time, then move those rows into the days they belong to and "
			+ "leave the default partition empty.");
	}

	private static StartupFailureView MapArchiveQueryTimedOut(ArchiveQueryTimedOutError error)
	{
		var read = FormattableString.Invariant($"The read of '{error.Database}' at {error.Host}:{error.Port}");

		var noBound = error.Timeout == TimeSpan.Zero;

		var detail = noBound
			? $"{read} was ended by the server (SQLSTATE 57014), which named no bound."
			: FormattableString.Invariant($"{read} passed the server's bound of {error.Timeout.TotalSeconds} s.");

		var remedy = noBound
			? "Check whether an administrator cancelled the read, and read statement_timeout for the "
				+ "reader role on the server — the answer carried no number to report."
			: "Raise statement_timeout for the reader role, or narrow the window SemiPlot opens on. A "
				+ "read this slow usually means the archive lacks an index on its time column.";

		return new StartupFailureView("The archive ended the read", detail, remedy);
	}

	private static StartupFailureView MapArchiveReadFailed(ArchiveReadFailedError error)
	{
		var named = !string.IsNullOrEmpty(error.SqlState);
		var archive = FormattableString.Invariant($"'{error.Database}' at {error.Host}:{error.Port}");

		var detail = named
			? $"The archive {archive} rejected the read (SQLSTATE {error.SqlState})."
			: $"The read of {archive} failed before the server answered, so it carries no SQLSTATE.";

		var remedy = named
			? $"This build has no named handling for SQLSTATE {error.SqlState}. Find the matching entry "
				+ "in the PostgreSQL server log and report it with the SemiPlot log file."
			: "The failure came from the client side. Report the SemiPlot log file, which carries the "
				+ "exception this build could not name.";

		return new StartupFailureView("The archive rejected the read", detail, remedy);
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

	// The exception equivalent of the catch-all arm. A startup step that throws instead of failing —
	// building the data source, or a read cancelled under the provider — carries its exception here, and
	// naming the type is what turns a silent exit into something an operator can report.
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
