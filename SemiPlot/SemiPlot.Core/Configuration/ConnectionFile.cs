using FluentResults;

using SemiPlot.Core.Data.Errors;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiPlot.Core.Configuration;

/// <summary>The parse and the field checks every connection section loader shares.</summary>
public static class ConnectionFile
{
	public const string HostKey = "host";

	public const string PortKey = "port";

	private static readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.IgnoreUnmatchedProperties()
		.Build();

	// The error carries no text of the exception: a parser message embeds the offending scalar, and a password
	// is a scalar. The raw detail rides on CausedBy, where only the log reads it.
	public static Result<TDto> Deserialize<TDto>(
		ConfigurationSectionName section,
		string sectionDirectory,
		string content)
		where TDto : class, new()
	{
		try
		{
			return Result.Ok(_deserializer.Deserialize<TDto?>(content) ?? new TDto());
		}
		catch (Exception exception)
		{
			var error = new ConnectionFileError(section, sectionDirectory, ConnectionFileProblem.Unparseable, []);

			return Result.Fail<TDto>(error.CausedBy(new ExceptionalError(exception)));
		}
	}

	/// <summary>
	/// Every blank text and an absent port as one missing-field error, the host first and the port last; then the
	/// port's range; then the host's IPv4 form.
	/// </summary>
	public static Result Validate(
		ConfigurationSectionName section,
		string sectionDirectory,
		string? host,
		int? port,
		IReadOnlyList<(string Key, string? Value)> otherTexts)
	{
		var missing = new List<string>();

		if (string.IsNullOrWhiteSpace(host))
		{
			missing.Add(HostKey);
		}

		foreach (var (key, value) in otherTexts)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				missing.Add(key);
			}
		}

		if (port is null)
		{
			missing.Add(PortKey);
		}

		if (missing.Count > 0)
		{
			return Invalid(section, sectionDirectory, ConnectionFileProblem.MissingField, missing);
		}

		if (port is < NetworkAddress.LowestPort or > NetworkAddress.HighestPort)
		{
			return Invalid(section, sectionDirectory, ConnectionFileProblem.OutOfRange, [PortKey]);
		}

		return NetworkAddress.IsIPv4(host)
			? Result.Ok()
			: Invalid(section, sectionDirectory, ConnectionFileProblem.HostNotIPv4, [HostKey]);
	}

	private static Result Invalid(
		ConfigurationSectionName section,
		string sectionDirectory,
		ConnectionFileProblem kind,
		IReadOnlyList<string> fieldNames)
	{
		return Result.Fail(new ConnectionFileError(section, sectionDirectory, kind, fieldNames));
	}
}
