using System.Globalization;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// Every member nullable so an absent field reaches the loader as a state to report; YAML keys follow
/// the underscored convention (<c>PollIntervalMs</c> reads <c>poll_interval_ms</c>).
/// </summary>
internal sealed class PostgresConnectionDto
{
	public string? Host { get; set; }

	public int? Port { get; set; }

	public string? Database { get; set; }

	public string? User { get; set; }

	public string? Password { get; set; }

	public int? PollIntervalMs { get; set; }

	public string? Schema { get; set; }
}

/// <summary>
/// Reads the archive connection section folder into <see cref="PostgresConnectionSettings"/>. Every
/// failure is a <see cref="ConnectionFileError"/> or a <see cref="ConfigurationSectionError"/> in the
/// result; nothing escapes as an exception, and keys the format does not name are ignored.
/// </summary>
public static class PostgresConnectionLoader
{
	public const string HostKey = "host";

	public const string PortKey = "port";

	public const string DatabaseKey = "database";

	public const string UserKey = "user";

	public const string PasswordKey = "password";

	public const string PollIntervalKey = "poll_interval_ms";

	public const int LowestPort = 1;

	public const int HighestPort = 65535;

	public const int LowestPollIntervalMs = 1;

	private const int HighestOctet = 255;

	private const string DefaultSchema = "public";

	private static readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.IgnoreUnmatchedProperties()
		.Build();

	public static Result<PostgresConnectionSettings> Load(string sectionDirectory)
	{
		var section = ConfigurationSection.Read(sectionDirectory, ConfigurationSectionName.Connection);

		if (section.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(section.Errors);
		}

		var read = Deserialize(sectionDirectory, section.Value);

		if (read.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(read.Errors);
		}

		var dto = read.Value;

		var fields = ValidateFields(sectionDirectory, dto);

		if (fields.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(fields.Errors);
		}

		var ranges = ValidateRanges(sectionDirectory, dto);

		if (ranges.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(ranges.Errors);
		}

		var host = ValidateHost(sectionDirectory, dto.Host);

		if (host.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(host.Errors);
		}

		return Result.Ok(Map(dto));
	}

	/// <summary>
	/// Four dot-separated decimal octets from 0 to 255 with no leading zero, so no shortened or octal form.
	/// </summary>
	public static bool IsIPv4Address(string? text)
	{
		if (text is null)
		{
			return false;
		}

		var octets = text.Split('.');

		return octets.Length == 4 && octets.All(IsOctet);
	}

	private static bool IsOctet(string octet)
	{
		return octet.Length is >= 1 and <= 3
			&& octet.All(char.IsAsciiDigit)
			&& (octet.Length == 1 || octet[0] != '0')
			&& int.Parse(octet, CultureInfo.InvariantCulture) <= HighestOctet;
	}

	private static PostgresConnectionSettings Map(PostgresConnectionDto dto)
	{
		return new PostgresConnectionSettings(
			dto.Host!,
			dto.Port!.Value,
			dto.Database!,
			dto.User!,
			dto.Password!,
			TimeZoneInfo.Local,
			TimeSpan.FromMilliseconds(dto.PollIntervalMs!.Value),
			dto.Schema ?? DefaultSchema);
	}

	// The reason never repeats what the exception said: a parser message embeds the offending scalar, and
	// the password is a scalar. The raw detail rides on CausedBy, where only the log reads it.
	private static Result<PostgresConnectionDto> Deserialize(string sectionDirectory, string content)
	{
		try
		{
			return Result.Ok(_deserializer.Deserialize<PostgresConnectionDto?>(content) ?? new PostgresConnectionDto());
		}
		catch (Exception exception)
		{
			var error = new ConnectionFileError(
				sectionDirectory,
				ConnectionFileProblem.Unparseable,
				"a key carries a value this format does not accept");

			return Result.Fail<PostgresConnectionDto>(error.CausedBy(new ExceptionalError(exception)));
		}
	}

	private static Result ValidateFields(string sectionDirectory, PostgresConnectionDto dto)
	{
		(string Name, string? Value)[] texts =
		[
			(HostKey, dto.Host),
			(DatabaseKey, dto.Database),
			(UserKey, dto.User),
			(PasswordKey, dto.Password)
		];

		var missing = new List<string>();

		foreach (var (name, value) in texts)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				missing.Add(name);
			}
		}

		if (dto.Port is null)
		{
			missing.Add(PortKey);
		}

		if (dto.PollIntervalMs is null)
		{
			missing.Add(PollIntervalKey);
		}

		return missing.Count == 0
			? Result.Ok()
			: Invalid(sectionDirectory, ConnectionFileProblem.MissingField, missing, "absent or blank");
	}

	private static Result ValidateRanges(string sectionDirectory, PostgresConnectionDto dto)
	{
		var outOfRange = new List<string>();

		if (dto.Port!.Value is < LowestPort or > HighestPort)
		{
			outOfRange.Add(PortKey);
		}

		if (dto.PollIntervalMs!.Value < LowestPollIntervalMs)
		{
			outOfRange.Add(PollIntervalKey);
		}

		return outOfRange.Count == 0
			? Result.Ok()
			: Invalid(sectionDirectory, ConnectionFileProblem.OutOfRange, outOfRange, "outside the range this build accepts");
	}

	private static Result ValidateHost(string sectionDirectory, string? host)
	{
		return IsIPv4Address(host)
			? Result.Ok()
			: Result.Fail(new ConnectionFileError(
				sectionDirectory,
				ConnectionFileProblem.HostNotIPv4,
				$"the field '{HostKey}' is not an IPv4 address of four decimal numbers from 0 to 255"));
	}

	private static Result Invalid(
		string sectionDirectory,
		ConnectionFileProblem kind,
		IReadOnlyCollection<string> fieldNames,
		string complaint)
	{
		var names = string.Join("', '", fieldNames);
		var subject = fieldNames.Count == 1 ? "field" : "fields";
		var verb = fieldNames.Count == 1 ? "is" : "are";

		return Result.Fail(
			new ConnectionFileError(sectionDirectory, kind, $"the required {subject} '{names}' {verb} {complaint}"));
	}
}
