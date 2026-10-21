using SemiPlot.Core.Configuration;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Messages;

/// <summary>The section half of <see cref="ArchiveFailureMapper"/>, which is its only caller.</summary>
public static class ConfigurationSectionFailureMapper
{
	public static ArchiveFailureView Map(ConfigurationSectionError error)
	{
		return new ArchiveFailureView(
			SectionTitle(error.Section),
			SectionDetail(error),
			SectionRemedy(error),
			MessageSeverity.Error);
	}

	private static string SectionTitle(ConfigurationSectionName section)
	{
		return section switch
		{
			ConfigurationSectionName.App => Resources.FailureConfigurationSectionAppTitle,
			ConfigurationSectionName.Connection => Resources.FailureConfigurationSectionConnectionTitle,
			ConfigurationSectionName.Opc => Resources.FailureConfigurationSectionOpcTitle,
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
		};
	}

	private static string SectionDetail(ConfigurationSectionError error)
	{
		return error.Problem switch
		{
			SectionProblem.DirectoryMissing =>
				Resources.FormatFailureConfigurationSectionDirectoryMissingDetail(error.Directory),

			SectionProblem.NoFiles =>
				Resources.FormatFailureConfigurationSectionNoFilesDetail(error.Directory),

			SectionProblem.DuplicateKey =>
				Resources.FormatFailureConfigurationSectionDuplicateKeyDetail(
					error.Key,
					error.FileNames[0],
					error.Directory),

			SectionProblem.KeyConflict =>
				Resources.FormatFailureConfigurationSectionKeyConflictDetail(
					error.Key,
					error.FileNames[0],
					error.FileNames[1],
					error.Directory),

			// Listing the folder is what failed, so no file can be named.
			SectionProblem.Unlistable =>
				Resources.FormatFailureConfigurationSectionUnlistableDetail(error.Directory),

			SectionProblem.Unreadable => Resources.FormatFailureConfigurationSectionUnreadableDetail(
				error.Directory, error.FileNames[0]),

			SectionProblem.Unwritable => Resources.FormatFailureConfigurationSectionUnwritableDetail(
				error.Directory, error.FileNames[0]),

			SectionProblem.KeyAbsent => Resources.FormatFailureConfigurationSectionKeyAbsentDetail(
				error.Key, error.Directory),

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Problem, null)
		};
	}

	private static string SectionRemedy(ConfigurationSectionError error)
	{
		return error.Problem switch
		{
			SectionProblem.DirectoryMissing => DirectoryMissingRemedy(error.Section),

			SectionProblem.NoFiles => NoFilesRemedy(error.Section),

			SectionProblem.DuplicateKey => Resources.FailureConfigurationSectionDuplicateKeyRemedy,

			SectionProblem.KeyConflict => Resources.FailureConfigurationSectionKeyConflictRemedy,

			SectionProblem.Unlistable or SectionProblem.Unreadable =>
				Resources.FailureConfigurationSectionUnreadableRemedy,

			SectionProblem.Unwritable => Resources.FailureConfigurationSectionUnwritableRemedy,

			SectionProblem.KeyAbsent => Resources.FailureConfigurationSectionKeyAbsentRemedy,

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Problem, null)
		};
	}

	private static string DirectoryMissingRemedy(ConfigurationSectionName section)
	{
		return section switch
		{
			ConfigurationSectionName.App => Resources.FailureConfigurationSectionAppDirectoryMissingRemedy,
			ConfigurationSectionName.Connection =>
				Resources.FailureConfigurationSectionConnectionDirectoryMissingRemedy,
			ConfigurationSectionName.Opc => Resources.FailureConfigurationSectionOpcDirectoryMissingRemedy,
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
		};
	}

	private static string NoFilesRemedy(ConfigurationSectionName section)
	{
		return section switch
		{
			ConfigurationSectionName.App => Resources.FailureConfigurationSectionAppNoFilesRemedy,
			ConfigurationSectionName.Connection => Resources.FailureConfigurationSectionConnectionNoFilesRemedy,
			ConfigurationSectionName.Opc => Resources.FailureConfigurationSectionOpcNoFilesRemedy,
			_ => throw new ArgumentOutOfRangeException(nameof(section), section, null)
		};
	}
}
