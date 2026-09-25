using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Configuration;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>A temporary copy of the shipped set with a filled password, and the save and its checks over it.</summary>
internal sealed class SettingsSaveSandbox : IDisposable
{
	public const string ConnectionFileName = "connection.yaml";

	public SettingsSaveSandbox()
	{
		ConfigDirectory = Directory.CreateTempSubdirectory("semiplot-settings-save-").FullName;
		AppDirectory = Path.Combine(ConfigDirectory, StartupSequence.SettingsDirectoryName);
		ConnectionDirectory = Path.Combine(ConfigDirectory, StartupProbe.ConnectionDirectoryName);
		ShippedConfiguration.CopyTo(ConfigDirectory, "secret");
	}

	public string ConfigDirectory { get; }

	public string AppDirectory { get; }

	public string ConnectionDirectory { get; }

	public string ConnectionFile => Path.Combine(ConnectionDirectory, ConnectionFileName);

	public void Dispose()
	{
		foreach (var file in Directory.GetFiles(ConfigDirectory, "*", SearchOption.AllDirectories))
		{
			File.SetAttributes(file, FileAttributes.Normal);
		}

		Directory.Delete(ConfigDirectory, recursive: true);
	}

	public static Dictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>> Edit(
		ConfigurationSectionName section,
		params (string Key, string Value)[] edits)
	{
		return new Dictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>>
		{
			[section] = edits.ToDictionary(edit => edit.Key, edit => edit.Value)
		};
	}

	public static TError SingleError<TError>(Result result)
		where TError : IError
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.Should().ContainSingle().Which.Should().BeOfType<TError>().Subject;
	}

	public static string Describe(IResultBase result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}

	public string DirectoryOf(ConfigurationSectionName section)
	{
		return section == ConfigurationSectionName.App ? AppDirectory : ConnectionDirectory;
	}

	public Result Save(IReadOnlyDictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>> edits)
	{
		return SettingsSave.Save(ConfigDirectory, edits, NullLogger.Instance);
	}

	public PostgresConnectionSettings LoadConnection()
	{
		var loaded = PostgresConnectionLoader.Load(ConnectionDirectory);

		loaded.IsSuccess.Should().BeTrue(Describe(loaded));

		return loaded.Value;
	}

	public Dictionary<string, byte[]> Snapshot()
	{
		return Directory.GetFiles(ConfigDirectory, "*", SearchOption.AllDirectories)
			.ToDictionary(file => Path.GetRelativePath(ConfigDirectory, file), File.ReadAllBytes);
	}

	/// <summary>Every file is byte-identical to the snapshot and no staging folder remains.</summary>
	public void ShouldMatch(Dictionary<string, byte[]> before)
	{
		var after = Snapshot();

		after.Keys.Should().BeEquivalentTo(before.Keys);

		foreach (var (file, content) in before)
		{
			after[file].Should().Equal(content, file);
		}

		StagingDirectories().Should().BeEmpty();
	}

	public string[] StagingDirectories()
	{
		return Directory.GetDirectories(ConfigDirectory, SettingsSave.StagingPrefix + "*");
	}
}
