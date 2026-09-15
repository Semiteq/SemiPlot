using FluentResults;

namespace SemiPlot.UI.Startup;

public enum AppSettingsProblem
{
	Unreadable,
	KeyMissing,
	ValueInvalid
}

/// <summary>
/// The interface settings section could not be turned into settings. <see cref="Kind"/> is what the
/// operator's remedy routes on; <see cref="Key"/> names the offending key, never the section's own values.
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
			AppSettingsProblem.KeyMissing =>
				$"The interface settings folder '{path}' carries no '{key}' key.",
			AppSettingsProblem.ValueInvalid =>
				$"The interface settings folder '{path}' holds a '{key}' outside its values ({acceptedValues}).",
			_ =>
				$"The interface settings folder '{path}' cannot be read."
		};
	}
}
