using System.Diagnostics;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.Settings;

/// <summary>
/// Reads the section folders the settings window shows, and writes the operator's edits back once the production
/// loaders accept a staged copy. A section's failure names its real folder, not the staged copy; nothing escapes as
/// an exception.
/// </summary>
public static class SettingsSave
{
	internal const string StagingPrefix = ".settings-staging-";

	private const string ReplacedSuffix = ".replaced";

	// The staging folder belongs to no section, and the error still needs one for its title.
	private const ConfigurationSectionName StagingFolderSection = ConfigurationSectionName.App;

	private static readonly SectionLoader[] _sections =
	[
		new(
			ConfigurationSectionName.App,
			StartupSequence.SettingsDirectoryName,
			directory => AppSettingsLoader.Load(directory).ToResult()),
		new(
			ConfigurationSectionName.Connection,
			StartupProbe.ConnectionDirectoryName,
			directory => PostgresConnectionLoader.Load(directory).ToResult())
	];

	/// <summary>Both sections as the files carry them, never through the typed loaders.</summary>
	internal static (Result<OwnedSection> App, Result<OwnedSection> Connection) ReadOwned(string configDirectory)
	{
		return (Read(ConfigurationSectionName.App), Read(ConfigurationSectionName.Connection));

		Result<OwnedSection> Read(ConfigurationSectionName section)
		{
			return ConfigurationSection.ReadOwned(SectionDirectory(configDirectory, section), section);
		}
	}

	private static string SectionDirectory(string configDirectory, ConfigurationSectionName section)
	{
		return Path.Combine(configDirectory, _sections.Single(loader => loader.Section == section).DirectoryName);
	}

	public static Result Save(
		string configDirectory,
		IReadOnlyDictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>> edits,
		ILogger logger)
	{
		var stagingDirectory = Path.Combine(configDirectory, StagingPrefix + Path.GetRandomFileName());
		var created = CreateStagingDirectory(configDirectory, stagingDirectory);

		if (created.IsFailed)
		{
			return created;
		}

		var staged = new List<PromotableSection>();
		var saved = StageAndPromote(configDirectory, stagingDirectory, edits, staged);
		var missingTarget = saved.IsFailed ? FindMissingTarget(staged) : null;

		if (missingTarget is null)
		{
			RemoveStagingDirectory(stagingDirectory, logger);
		}
		else
		{
			logger.LogError(
				"The settings staging folder {Directory} is kept because {Target} is missing after a failed save",
				stagingDirectory,
				missingTarget);
		}

		return saved;
	}

	private static Result StageAndPromote(
		string configDirectory,
		string stagingDirectory,
		IReadOnlyDictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>> edits,
		List<PromotableSection> staged)
	{
		foreach (var loader in _sections)
		{
			var section = StageSection(loader, configDirectory, stagingDirectory, edits);

			if (section.IsFailed)
			{
				return section.ToResult();
			}

			staged.Add(section.Value);
		}

		var probed = ProbeTargets(staged);

		return probed.IsFailed ? probed : Promote(staged);
	}

	private static Result<PromotableSection> StageSection(
		SectionLoader loader,
		string configDirectory,
		string stagingDirectory,
		IReadOnlyDictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>> edits)
	{
		var sectionDirectory = Path.Combine(configDirectory, loader.DirectoryName);
		var stagedDirectory = Path.Combine(stagingDirectory, loader.DirectoryName);
		var owned = ConfigurationSection.ReadOwned(sectionDirectory, loader.Section);

		if (owned.IsFailed)
		{
			return Result.Fail<PromotableSection>(owned.Errors);
		}

		var sectionEdits = edits.GetValueOrDefault(loader.Section) ?? new Dictionary<string, string>();
		var rewritten = ConfigurationSectionWriter.Stage(
			owned.Value, sectionEdits, loader.Section, sectionDirectory, stagedDirectory);

		if (rewritten.IsFailed)
		{
			return Result.Fail<PromotableSection>(rewritten.Errors);
		}

		var loaded = loader.Load(stagedDirectory);

		if (loaded.IsFailed)
		{
			return Result.Fail<PromotableSection>(
				loaded.Errors.Select(error => InRealDirectory(error, sectionDirectory)));
		}

		return Result.Ok(new PromotableSection(loader.Section, sectionDirectory, stagedDirectory, rewritten.Value));
	}

	private static IError InRealDirectory(IError error, string sectionDirectory)
	{
		Error rebuilt = error switch
		{
			ConfigurationSectionError section => new ConfigurationSectionError(
				section.Section, sectionDirectory, section.Problem, section.Key, section.FileNames),
			AppSettingsError settings => new AppSettingsError(
				sectionDirectory, settings.Kind, settings.Key, settings.AcceptedValues),
			ConnectionFileError file => new ConnectionFileError(sectionDirectory, file.Kind, file.Reason),
			_ => throw new UnreachableException($"A section loader returned {error.GetType().Name}.")
		};

		return rebuilt.CausedBy(error.Reasons);
	}

	// Opening with FileMode.Open never truncates, so a target that accepts the probe keeps its content.
	private static Result ProbeTargets(IReadOnlyList<PromotableSection> staged)
	{
		foreach (var section in staged)
		{
			foreach (var name in section.Rewritten)
			{
				try
				{
					new FileStream(Path.Combine(section.Directory, name), FileMode.Open, FileAccess.Write).Dispose();
				}
				catch (Exception exception)
				{
					return Unwritable(section, name, exception);
				}
			}
		}

		return Result.Ok();
	}

	private static Result Promote(IReadOnlyList<PromotableSection> staged)
	{
		foreach (var section in staged)
		{
			foreach (var name in section.Rewritten)
			{
				try
				{
					Replace(
						Path.Combine(section.StagedDirectory, name),
						Path.Combine(section.Directory, name),
						Path.Combine(section.StagedDirectory, name + ReplacedSuffix));
				}
				catch (Exception exception)
				{
					return Unwritable(section, name, exception);
				}
			}
		}

		return Result.Ok();
	}

	// File.Replace keeps the target's ACL and attributes on Windows; on Unix it is a rename, which keeps
	// neither, so the target's mode goes onto the staged file first.
	private static void Replace(string staged, string target, string replaced)
	{
		if (!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(staged, File.GetUnixFileMode(target));
		}

		File.Replace(staged, target, replaced);
	}

	// A replace that fails after moving its target leaves the target only as its copy in the staging folder.
	private static string? FindMissingTarget(IReadOnlyList<PromotableSection> staged)
	{
		return staged
			.SelectMany(section => section.Rewritten.Select(name => Path.Combine(section.Directory, name)))
			.FirstOrDefault(target => !File.Exists(target));
	}

	private static Result CreateStagingDirectory(string configDirectory, string stagingDirectory)
	{
		try
		{
			foreach (var loader in _sections)
			{
				Directory.CreateDirectory(Path.Combine(stagingDirectory, loader.DirectoryName));
			}

			return Result.Ok();
		}
		catch (Exception exception)
		{
			return ConfigurationSection.Fail(
				StagingFolderSection,
				configDirectory,
				SectionProblem.Unwritable,
				fileNames: [Path.GetFileName(stagingDirectory)],
				cause: exception);
		}
	}

	private static void RemoveStagingDirectory(string stagingDirectory, ILogger logger)
	{
		try
		{
			Directory.Delete(stagingDirectory, recursive: true);
		}
		catch (Exception exception)
		{
			logger.LogWarning(
				exception, "The settings staging folder {Directory} could not be removed", stagingDirectory);
		}
	}

	private static Result Unwritable(PromotableSection section, string name, Exception cause)
	{
		return ConfigurationSection.Fail(
			section.Section, section.Directory, SectionProblem.Unwritable, fileNames: [name], cause: cause);
	}

	private sealed record SectionLoader(
		ConfigurationSectionName Section,
		string DirectoryName,
		Func<string, Result> Load);

	private sealed record PromotableSection(
		ConfigurationSectionName Section,
		string Directory,
		string StagedDirectory,
		IReadOnlyList<string> Rewritten);
}
