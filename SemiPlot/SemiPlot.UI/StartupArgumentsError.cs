using FluentResults;

namespace SemiPlot.UI;

public enum StartupArgumentsProblem
{
	Missing,
	ValueMissing,
	Unknown,
	ValueInvalid
}

/// <summary>
/// The launch arguments could not be turned into <see cref="StartupOptions"/>. <see cref="Kind"/> is
/// what the operator's remedy routes on; <see cref="Key"/> names the argument, never its value.
/// </summary>
public sealed class StartupArgumentsError(
	StartupArgumentsProblem kind,
	string key,
	string acceptedValues = "")
	: Error(Describe(kind, key, acceptedValues))
{
	public StartupArgumentsProblem Kind { get; } = kind;

	public string Key { get; } = key;

	public string AcceptedValues { get; } = acceptedValues;

	private static string Describe(StartupArgumentsProblem kind, string key, string acceptedValues)
	{
		return kind switch
		{
			StartupArgumentsProblem.Missing =>
				$"SemiPlot was started without the required key '{key}'.",
			StartupArgumentsProblem.ValueMissing =>
				$"The key '{key}' was given with no value after it.",
			StartupArgumentsProblem.ValueInvalid =>
				$"The key '{key}' holds a value outside its set ({acceptedValues}).",
			_ =>
				$"SemiPlot does not know the argument '{key}'."
		};
	}
}
