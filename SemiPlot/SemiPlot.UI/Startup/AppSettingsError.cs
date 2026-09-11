using FluentResults;

namespace SemiPlot.UI.Startup;

public enum AppSettingsProblem
{
	NotFound,
	Unreadable,
	KeyMissing,
	ValueInvalid
}

/// <summary>
/// The interface settings file could not be turned into settings. <see cref="Kind"/> is what the
/// operator's remedy routes on; <see cref="Key"/> names the offending key, never the file's own values.
/// </summary>
public sealed class AppSettingsError(
	string path,
	AppSettingsProblem kind,
	string key = "",
	string acceptedValues = "")
	: Error(Describe(path, kind, key, acceptedValues))
{
	public string Path { get; } = path;

	public AppSettingsProblem Kind { get; } = kind;

	public string Key { get; } = key;

	public string AcceptedValues { get; } = acceptedValues;

	private static string Describe(string path, AppSettingsProblem kind, string key, string acceptedValues)
	{
		return kind switch
		{
			AppSettingsProblem.NotFound =>
				$"The interface settings file '{path}' does not exist.",
			AppSettingsProblem.KeyMissing =>
				$"The interface settings file '{path}' carries no '{key}' key.",
			AppSettingsProblem.ValueInvalid =>
				$"The interface settings file '{path}' holds a '{key}' outside its values ({acceptedValues}).",
			_ =>
				$"The interface settings file '{path}' cannot be read."
		};
	}
}
