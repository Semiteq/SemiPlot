using FluentResults;

namespace SemiPlot.Core.Data.Errors;

public enum ConnectionFileProblem
{
	Unparseable,
	MissingField,
	OutOfRange,
	HostNotIPv4
}

/// <summary>
/// The connection section could not be turned into settings. <see cref="Kind"/> is what the operator's
/// remedy routes on; <see cref="Fields"/> names the offending keys, never the section's own values.
/// </summary>
public sealed class ConnectionFileError(string path, ConnectionFileProblem kind, IReadOnlyList<string> fields)
	: Error(Describe(path, kind, fields))
{
	public string Path { get; } = path;

	public ConnectionFileProblem Kind { get; } = kind;

	public IReadOnlyList<string> Fields { get; } = fields;

	private static string Describe(string path, ConnectionFileProblem kind, IReadOnlyList<string> fields)
	{
		var message = $"The connection folder '{path}' is invalid ({kind})";
		return fields.Count > 0 ? $"{message}; fields: {string.Join(", ", fields)}" : message;
	}
}
