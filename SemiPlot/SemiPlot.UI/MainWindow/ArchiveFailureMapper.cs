using FluentResults;

using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.MainWindow;

/// <summary>
/// Turns an <see cref="IError"/> into the state the operator reads: three blocks through <see cref="Map"/>,
/// one line through <see cref="Describe"/>. The only place a remedy is written.
/// </summary>
public static class ArchiveFailureMapper
{
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
			AppSettingsError settings => MapAppSettings(settings),
			ConnectionFileError file => MapConnectionFile(file),
			ArchiveError archive => MapArchive(archive),
			StartupReadTimedOutError startupTimeout => MapStartupReadTimedOut(startupTimeout),
			IExceptionalError thrown => MapThrown(thrown),
			_ => MapUnknown(error)
		};
	}

	// This window is read in the bootstrap language: no locale exists yet when the settings file itself
	// is what failed.
	private static ArchiveFailureView MapAppSettings(AppSettingsError error)
	{
		return error.Kind switch
		{
			AppSettingsProblem.NotFound => new ArchiveFailureView(
				Resources.FailureAppSettingsNotFoundTitle,
				Resources.FormatFailureAppSettingsNotFoundDetail(error.Path),
				Resources.FailureAppSettingsNotFoundRemedy),

			AppSettingsProblem.KeyMissing => new ArchiveFailureView(
				Resources.FailureAppSettingsRejectedTitle,
				Resources.FormatFailureAppSettingsKeyMissingDetail(error.Path, error.Key),
				Resources.FailureAppSettingsKeyMissingRemedy),

			AppSettingsProblem.ValueInvalid => new ArchiveFailureView(
				Resources.FailureAppSettingsRejectedTitle,
				Resources.FormatFailureAppSettingsValueInvalidDetail(error.Path, error.Key, error.AcceptedValues),
				Resources.FailureAppSettingsValueInvalidRemedy),

			AppSettingsProblem.Unreadable => new ArchiveFailureView(
				Resources.FailureAppSettingsRejectedTitle,
				Resources.FormatFailureAppSettingsUnreadableDetail(error.Path),
				Resources.FailureAppSettingsUnreadableRemedy),

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Kind, null)
		};
	}

	private static ArchiveFailureView MapConnectionFile(ConnectionFileError error)
	{
		if (error.Kind == ConnectionFileProblem.NotFound)
		{
			return new ArchiveFailureView(
				Resources.FailureConnectionFileNotFoundTitle,
				Resources.FormatFailureConnectionFileNotFoundDetail(error.Path),
				Resources.FailureConnectionFileNotFoundRemedy);
		}

		return new ArchiveFailureView(
			Resources.FailureConnectionFileRejectedTitle,
			Resources.FormatFailureConnectionFileRejectedDetail(error.Path, error.Reason),
			error.Kind switch
			{
				ConnectionFileProblem.Unreadable => Resources.FailureConnectionFileUnreadableRemedy,
				ConnectionFileProblem.Unparseable => Resources.FailureConnectionFileUnparseableRemedy,
				ConnectionFileProblem.MissingField => Resources.FailureConnectionFileMissingFieldRemedy,
				ConnectionFileProblem.OutOfRange => Resources.FailureConnectionFileOutOfRangeRemedy,
				ConnectionFileProblem.UnknownTimeZone => Resources.FailureConnectionFileUnknownTimeZoneRemedy,
				_ => Resources.FailureConnectionFileRejectedRemedy
			});
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
				Resources.FailureArchiveUnreachableRemedy),

			// 28P01 and 28000 are raised before PostgreSQL looks at the database name, so the archive was
			// never confirmed to exist.
			ArchiveFault.AccessDenied => new ArchiveFailureView(
				Resources.FailureArchiveAccessDeniedTitle,
				Resources.FormatFailureArchiveAccessDeniedDetail(address, error.Detail, error.Database),
				Resources.FailureArchiveAccessDeniedRemedy),

			// A wrong database name reaches the server and looks the same as an unprovisioned one.
			ArchiveFault.DatabaseMissing => new ArchiveFailureView(
				Resources.FailureArchiveNotProvisionedTitle,
				Resources.FormatFailureArchiveDatabaseMissingDetail(address, error.Database),
				Resources.FailureArchiveDatabaseMissingRemedy),

			// One provisioning run creates every table SemiPlot reads, so the remedy never depends on which
			// table is absent.
			ArchiveFault.TableMissing => new ArchiveFailureView(
				Resources.FailureArchiveNotProvisionedTitle,
				Resources.FormatFailureArchiveTableMissingDetail(archive, error.Detail),
				Resources.FormatFailureArchiveTableMissingRemedy(error.Detail)),

			// A lost live edge never opens the startup failure panel: it is drawn as a banner over a chart
			// that keeps its history, so the words say what is still true as well as what failed.
			ArchiveFault.ConnectionLost => new ArchiveFailureView(
				Resources.FailureArchiveConnectionLostTitle,
				Resources.FormatFailureArchiveConnectionLostDetail(archive, error.Detail),
				Resources.FailureArchiveConnectionLostRemedy),

			ArchiveFault.ShapeUnexpected => new ArchiveFailureView(
				Resources.FailureArchiveShapeUnexpectedTitle,
				Resources.FormatFailureArchiveShapeUnexpectedDetail(archive, error.Detail),
				Resources.FailureArchiveShapeUnexpectedRemedy),

			ArchiveFault.QueryTimedOut => new ArchiveFailureView(
				Resources.FailureArchiveQueryTimedOutTitle,
				Resources.FormatFailureArchiveQueryTimedOutDetail(archive),
				Resources.FailureArchiveQueryTimedOutRemedy),

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
				Resources.FailureArchiveReadUnnamedRemedy);
		}

		return new ArchiveFailureView(
			Resources.FailureArchiveReadFailedTitle,
			Resources.FormatFailureArchiveReadFailedDetail(archive, error.Detail),
			Resources.FormatFailureArchiveReadFailedRemedy(error.Detail));
	}

	private static ArchiveFailureView MapStartupReadTimedOut(StartupReadTimedOutError error)
	{
		return new ArchiveFailureView(
			Resources.FailureStartupReadTimedOutTitle,
			Resources.FormatFailureStartupReadTimedOutDetail(NameOf(error.Read), error.Bound.TotalSeconds),
			Resources.FailureStartupReadTimedOutRemedy);
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
			Resources.FailureThrownRemedy);
	}

	private static ArchiveFailureView MapUnknown(IError error)
	{
		return new ArchiveFailureView(
			Resources.FailureGenericTitle,
			error.Message,
			Resources.FailureUnknownRemedy);
	}
}
