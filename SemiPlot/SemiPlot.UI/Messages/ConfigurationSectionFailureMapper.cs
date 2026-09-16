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

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Problem, null)
		};
	}

	private static string SectionRemedy(ConfigurationSectionError error)
	{
		var app = error.Section is ConfigurationSectionName.App;

		return error.Problem switch
		{
			SectionProblem.DirectoryMissing => app
				? Resources.FailureConfigurationSectionAppDirectoryMissingRemedy
				: Resources.FailureConfigurationSectionConnectionDirectoryMissingRemedy,

			SectionProblem.NoFiles => app
				? Resources.FailureConfigurationSectionAppNoFilesRemedy
				: Resources.FailureConfigurationSectionConnectionNoFilesRemedy,

			SectionProblem.DuplicateKey => Resources.FailureConfigurationSectionDuplicateKeyRemedy,

			SectionProblem.KeyConflict => Resources.FailureConfigurationSectionKeyConflictRemedy,

			SectionProblem.Unlistable or SectionProblem.Unreadable =>
				Resources.FailureConfigurationSectionUnreadableRemedy,

			_ => throw new ArgumentOutOfRangeException(nameof(error), error.Problem, null)
		};
	}
}
