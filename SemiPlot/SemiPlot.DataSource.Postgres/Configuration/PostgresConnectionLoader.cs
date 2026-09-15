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

	public string? SourceTimeZone { get; set; }

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
	private const int LowestPort = 1;

	private const int HighestPort = 65535;

	private const string PortKey = "port";

	private const string PollIntervalKey = "poll_interval_ms";

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

		var zone = ResolveTimeZone(sectionDirectory, dto.SourceTimeZone!);

		if (zone.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(zone.Errors);
		}

		return Result.Ok(Map(dto, zone.Value));
	}

	private static PostgresConnectionSettings Map(PostgresConnectionDto dto, TimeZoneInfo sourceTimeZone)
	{
		return new PostgresConnectionSettings(
			dto.Host!,
			dto.Port!.Value,
			dto.Database!,
			dto.User!,
			dto.Password!,
			sourceTimeZone,
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
			return Fail<PostgresConnectionDto>(
				sectionDirectory,
				ConnectionFileProblem.Unparseable,
				"a key carries a value this format does not accept",
				exception);
		}
	}

	private static Result ValidateFields(string sectionDirectory, PostgresConnectionDto dto)
	{
		(string Name, string? Value)[] texts =
		[
			("host", dto.Host),
			("database", dto.Database),
			("user", dto.User),
			("password", dto.Password),
			("source_time_zone", dto.SourceTimeZone)
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

		if (dto.PollIntervalMs!.Value <= 0)
		{
			outOfRange.Add(PollIntervalKey);
		}

		return outOfRange.Count == 0
			? Result.Ok()
			: Invalid(sectionDirectory, ConnectionFileProblem.OutOfRange, outOfRange, "outside the range this build accepts");
	}

	private static Result<TimeZoneInfo> ResolveTimeZone(string sectionDirectory, string identifier)
	{
		try
		{
			return Result.Ok(TimeZoneInfo.FindSystemTimeZoneById(identifier));
		}
		catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			return Fail<TimeZoneInfo>(
				sectionDirectory,
				ConnectionFileProblem.UnknownTimeZone,
				$"'{identifier}' is not a time zone this machine knows",
				exception);
		}
	}

	private static Result<TValue> Fail<TValue>(
		string sectionDirectory,
		ConnectionFileProblem kind,
		string reason,
		Exception cause)
	{
		var error = new ConnectionFileError(sectionDirectory, kind, reason);

		return Result.Fail<TValue>(error.CausedBy(new ExceptionalError(cause)));
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
