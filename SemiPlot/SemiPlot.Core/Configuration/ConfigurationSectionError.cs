using FluentResults;

namespace SemiPlot.Core.Configuration;

/// <summary>The section a failure belongs to, so a remedy can name what the folder configures.</summary>
public enum ConfigurationSectionName
{
	App,
	Connection
}

public enum SectionProblem
{
	DirectoryMissing,
	NoFiles,
	Unlistable,
	Unreadable,
	DuplicateKey,
	KeyConflict,
	Unwritable,
	KeyAbsent
}

/// <summary>
/// A section folder could not be turned into one mapping. <see cref="Problem"/> is what the operator's
/// remedy routes on; <see cref="Key"/> and <see cref="FileNames"/> name what collided, never a value.
/// </summary>
public sealed class ConfigurationSectionError(
	ConfigurationSectionName section,
	string directory,
	SectionProblem problem,
	string key = "",
	IReadOnlyList<string>? fileNames = null)
	: Error(Describe(section, directory, problem, key, fileNames ?? []))
{
	public ConfigurationSectionName Section { get; } = section;

	public string Directory { get; } = directory;

	public SectionProblem Problem { get; } = problem;

	public string Key { get; } = key;

	public IReadOnlyList<string> FileNames { get; } = fileNames ?? [];

	private static string Describe(
		ConfigurationSectionName section,
		string directory,
		SectionProblem problem,
		string key,
		IReadOnlyList<string> fileNames)
	{
		var files = string.Join("', '", fileNames);

		return problem switch
		{
			SectionProblem.DirectoryMissing =>
				$"The {section} configuration folder '{directory}' does not exist.",
			SectionProblem.NoFiles =>
				$"The {section} configuration folder '{directory}' holds no '*.yaml' file.",
			SectionProblem.DuplicateKey =>
				$"The {section} configuration folder '{directory}' carries '{key}' twice in '{files}'.",
			SectionProblem.KeyConflict =>
				$"The {section} configuration folder '{directory}' carries '{key}' in both '{files}'.",
			SectionProblem.Unlistable =>
				$"The {section} configuration folder '{directory}' could not be listed.",
			SectionProblem.Unwritable =>
				$"The {section} configuration folder '{directory}' holds a file '{files}' that could not be written.",
			SectionProblem.KeyAbsent =>
				$"The {section} configuration folder '{directory}' carries '{key}' in no file.",
			_ =>
				$"The {section} configuration folder '{directory}' holds an unreadable file '{files}'."
		};
	}
}
