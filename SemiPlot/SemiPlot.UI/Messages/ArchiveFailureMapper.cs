using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.Messages;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads: a title, a detail and a remedy.
/// The only place a remedy is written.
/// </summary>
public static class ArchiveFailureMapper
{
	public static ArchiveFailureView Map(IError error)
	{
		return error switch
		{
			StartupArgumentsError arguments => MapStartupArguments(arguments),
			LogFileError logFile => MapLogFile(logFile),
			ConfigurationSectionError section => ConfigurationSectionFailureMapper.Map(section),
			AppSettingsError settings => MapAppSettings(settings),
			ConnectionFileError file => MapConnectionFile(file),
			ArchiveError archive => MapArchive(archive),
			StartupReadTimedOutError startupTimeout => MapStartupReadTimedOut(startupTimeout),
			IExceptionalError thrown => MapThrown(thrown),
			_ => MapUnknown(error)
		};
	}

	// A windowed executable has no standard error stream an operator ever sees, so a wrong shortcut is
	// reported here like every other startup failure.
	private static ArchiveFailureView MapStartupArguments(StartupArgumentsError error)
	{
		return error.Kind switch
		{
			StartupArgumentsProblem.Missing => new ArchiveFailureView(
				Resources.FailureStartupArgumentsTitle,
				Resources.FormatFailureStartupArgumentsMissingDetail(error.Key),
				Resources.FailureStartupArgumentsMissingRemedy,
				MessageSeverity.Error),

			StartupArgumentsProblem.ValueMissing => new ArchiveFailureView(
				Resources.FailureStartupArgumentsTitle,
				Resources.FormatFailureStartupArgumentsValueMissingDetail(error.Key),
				Resources.FailureStartupArgumentsValueMissingRemedy,
				MessageSeverity.Error),

			StartupArgumentsProblem.ValueInvalid => new ArchiveFailureView(
				Resources.FailureStartupArgumentsTitle,
				Resources.FormatFailureStartupArgumentsValueInvalidDetail(error.Key, error.AcceptedValues),
				Resources.FailureStartupArgumentsValueInvalidRemedy,
				MessageSeverity.Error),

			StartupArgumentsProblem.Unknown => new ArchiveFailureView(
				Resources.FailureStartupArgumentsTitle,
				Resources.FormatFailureStartupArgumentsUnknownDetail(error.Key),
				Resources.FailureStartupArgumentsUnknownRemedy,
				MessageSeverity.Error),

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, null)
		};
	}

	private static ArchiveFailureView MapLogFile(LogFileError error)
	{
		return new ArchiveFailureView(
			Resources.FailureLogFileTitle,
			Resources.FormatFailureLogFileDetail(error.FilePath, error.Reason),
			Resources.FailureLogFileRemedy,
			MessageSeverity.Error);
	}

	// This window is read in the bootstrap language: no locale exists yet when the settings section
	// itself is what failed.
	private static ArchiveFailureView MapAppSettings(AppSettingsError error)
	{
		return error.Kind switch
		{
			AppSettingsProblem.KeyMissing => new ArchiveFailureView(
				Resources.FailureAppSettingsRejectedTitle,
				Resources.FormatFailureAppSettingsKeyMissingDetail(error.Path, error.Key),
				Resources.FailureAppSettingsKeyMissingRemedy,
				MessageSeverity.Error),

			AppSettingsProblem.ValueInvalid => new ArchiveFailureView(
				Resources.FailureAppSettingsRejectedTitle,
				Resources.FormatFailureAppSettingsValueInvalidDetail(error.Path, error.Key, error.AcceptedValues),
				Resources.FailureAppSettingsValueInvalidRemedy,
				MessageSeverity.Error),

			AppSettingsProblem.Unreadable => new ArchiveFailureView(
				Resources.FailureAppSettingsRejectedTitle,
				Resources.FormatFailureAppSettingsUnreadableDetail(error.Path),
				Resources.FailureAppSettingsUnreadableRemedy,
				MessageSeverity.Error),

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, null)
		};
	}

	private static ArchiveFailureView MapConnectionFile(ConnectionFileError error)
	{
		return new ArchiveFailureView(
			Resources.FailureConnectionFileRejectedTitle,
			Resources.FormatFailureConnectionFileRejectedDetail(error.Path, error.Reason),
			error.Kind switch
			{
				ConnectionFileProblem.Unparseable => Resources.FailureConnectionFileUnparseableRemedy,
				ConnectionFileProblem.MissingField => Resources.FailureConnectionFileMissingFieldRemedy,
				ConnectionFileProblem.OutOfRange => Resources.FailureConnectionFileOutOfRangeRemedy,
				ConnectionFileProblem.HostNotIPv4 => Resources.FailureConnectionFileHostNotIPv4Remedy,
				_ => Resources.FailureConnectionFileRejectedRemedy
			},
			MessageSeverity.Error);
	}

	private static ArchiveFailureView MapArchive(ArchiveError error)
	{
		var address = FormattableString.Invariant($"{error.Host}:{error.Port}");
		var archive = Resources.FormatFailureArchiveNameFormat(error.Database, address);

		return error.Kind switch
		{
			ArchiveFault.Unreachable => new ArchiveFailureView(
				Resources.FailureArchiveUnreachableTitle,
				Resources.FormatFailureArchiveUnreachableDetail(archive),
				Resources.FailureArchiveUnreachableRemedy,
				MessageSeverity.Warning),

			// 28P01 and 28000 are raised before PostgreSQL looks at the database name, so the archive was
			// never confirmed to exist.
			ArchiveFault.AccessDenied => new ArchiveFailureView(
				Resources.FailureArchiveAccessDeniedTitle,
				Resources.FormatFailureArchiveAccessDeniedDetail(address, error.Detail, error.Database),
				Resources.FailureArchiveAccessDeniedRemedy,
				MessageSeverity.Error),

			// A wrong database name reaches the server and looks the same as an unprovisioned one.
			ArchiveFault.DatabaseMissing => new ArchiveFailureView(
				Resources.FailureArchiveNotProvisionedTitle,
				Resources.FormatFailureArchiveDatabaseMissingDetail(address, error.Database),
				Resources.FailureArchiveDatabaseMissingRemedy,
				MessageSeverity.Error),

			// One provisioning run creates every table SemiPlot reads, so the remedy never depends on which
			// table is absent.
			ArchiveFault.TableMissing => new ArchiveFailureView(
				Resources.FailureArchiveNotProvisionedTitle,
				Resources.FormatFailureArchiveTableMissingDetail(archive, error.Detail),
				Resources.FormatFailureArchiveTableMissingRemedy(error.Detail),
				MessageSeverity.Error),

			// A lost live edge never opens the startup failure panel: it is drawn over a chart that keeps
			// its history, so the words say what is still true as well as what failed.
			ArchiveFault.ConnectionLost => new ArchiveFailureView(
				Resources.FailureArchiveConnectionLostTitle,
				Resources.FormatFailureArchiveConnectionLostDetail(archive, error.Detail),
				Resources.FailureArchiveConnectionLostRemedy,
				MessageSeverity.Warning),

			ArchiveFault.ShapeUnexpected => new ArchiveFailureView(
				Resources.FailureArchiveShapeUnexpectedTitle,
				Resources.FormatFailureArchiveShapeUnexpectedDetail(archive, error.Detail),
				Resources.FailureArchiveShapeUnexpectedRemedy,
				MessageSeverity.Error),

			ArchiveFault.QueryTimedOut => new ArchiveFailureView(
				Resources.FailureArchiveQueryTimedOutTitle,
				Resources.FormatFailureArchiveQueryTimedOutDetail(archive),
				Resources.FailureArchiveQueryTimedOutRemedy,
				MessageSeverity.Warning),

			_ => MapReadFailed(error, archive)
		};
	}

	private static ArchiveFailureView MapReadFailed(ArchiveError error, string archive)
	{
		if (error.Detail.Length == 0)
		{
			return new ArchiveFailureView(
				Resources.FailureArchiveReadFailedTitle,
				Resources.FormatFailureArchiveReadUnnamedDetail(archive),
				Resources.FailureArchiveReadUnnamedRemedy,
				MessageSeverity.Warning);
		}

		return new ArchiveFailureView(
			Resources.FailureArchiveReadFailedTitle,
			Resources.FormatFailureArchiveReadFailedDetail(archive, error.Detail),
			Resources.FormatFailureArchiveReadFailedRemedy(error.Detail),
			MessageSeverity.Warning);
	}

	private static ArchiveFailureView MapStartupReadTimedOut(StartupReadTimedOutError error)
	{
		return new ArchiveFailureView(
			Resources.FailureStartupReadTimedOutTitle,
			Resources.FormatFailureStartupReadTimedOutDetail(NameOf(error.Read), error.Bound.TotalSeconds),
			Resources.FailureStartupReadTimedOutRemedy,
			MessageSeverity.Error);
	}

	private static string NameOf(StartupRead read)
	{
		return read switch
		{
			StartupRead.PenCatalogue => Resources.FailureStartupReadPenCatalogue,
			StartupRead.ArchiveExtent => Resources.FailureStartupReadArchiveExtent,
			_ => throw new ArgumentOutOfRangeException(nameof(read), read, null)
		};
	}

	private static ArchiveFailureView MapThrown(IExceptionalError error)
	{
		return new ArchiveFailureView(
			Resources.FailureThrownTitle,
			Resources.FormatFailureThrownDetail(error.Exception.GetType().Name, error.Exception.Message),
			Resources.FailureThrownRemedy,
			MessageSeverity.Error);
	}

	private static ArchiveFailureView MapUnknown(IError error)
	{
		return new ArchiveFailureView(
			Resources.FailureGenericTitle,
			error.Message,
			Resources.FailureUnknownRemedy,
			MessageSeverity.Error);
	}
}
