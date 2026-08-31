using FluentResults;

using SemiPlot.Core.Data.Errors;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// Reads the archive connection file into <see cref="PostgresConnectionSettings"/>. Every failure is a
/// <see cref="ConnectionFileError"/> in the returned <see cref="Result{TValue}"/>; nothing escapes as an
/// exception, for any input including a blank path. Keys the format does not name are ignored.
/// </summary>
public static class PostgresConnectionLoader
{
	private const int LowestPort = 1;

	private const int HighestPort = 65535;

	private const string PortKey = "port";

	private const string PollIntervalKey = "poll_interval_ms";

	private static readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.IgnoreUnmatchedProperties()
		.Build();

	public static Result<PostgresConnectionSettings> Load(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return Fail<PostgresConnectionSettings>(filePath, ConnectionFileProblem.NotFound);
		}

		var read = Read(filePath);

		if (read.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(read.Errors);
		}

		var dto = read.Value;

		var fields = ValidateFields(filePath, dto);

		if (fields.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(fields.Errors);
		}

		var ranges = ValidateRanges(filePath, dto);

		if (ranges.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(ranges.Errors);
		}

		var zone = ResolveTimeZone(filePath, dto.SourceTimeZone!);

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
			dto.Schema!);
	}

	// Neither reason repeats what the exception said: a parser message embeds the offending scalar, and
	// the password is a scalar. The raw detail rides on CausedBy, where only the log reads it.
	private static Result<PostgresConnectionDto> Read(string filePath)
	{
		string content;

		try
		{
			content = File.ReadAllText(filePath);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			return Fail<PostgresConnectionDto>(filePath, ConnectionFileProblem.NotFound, cause: exception);
		}
		catch (Exception exception)
		{
			return Fail<PostgresConnectionDto>(
				filePath, ConnectionFileProblem.Unreadable, "the file cannot be opened for reading", exception);
		}

		try
		{
			var dto = _deserializer.Deserialize<PostgresConnectionDto?>(content);

			return dto is null
				? Fail<PostgresConnectionDto>(
					filePath, ConnectionFileProblem.Unparseable, "the file carries no configuration")
				: Result.Ok(dto);
		}
		catch (Exception exception)
		{
			return Fail<PostgresConnectionDto>(
				filePath, ConnectionFileProblem.Unparseable, "the file is not valid YAML", exception);
		}
	}

	private static Result ValidateFields(string filePath, PostgresConnectionDto dto)
	{
		(string Name, string? Value)[] texts =
		[
			("host", dto.Host),
			("database", dto.Database),
			("user", dto.User),
			("password", dto.Password),
			("source_time_zone", dto.SourceTimeZone),
			("schema", dto.Schema)
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
			: Invalid(filePath, ConnectionFileProblem.MissingField, missing, "absent or blank");
	}

	// A port of 0 and a negative interval parse as integers and then detonate downstream — the Npgsql
	// builder rejects a port outside 1..65535 on assignment, and a non-positive interval becomes a
	// TimeSpan nothing checks.
	private static Result ValidateRanges(string filePath, PostgresConnectionDto dto)
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
			: Invalid(filePath, ConnectionFileProblem.OutOfRange, outOfRange, "outside the range this build accepts");
	}

	private static Result<TimeZoneInfo> ResolveTimeZone(string filePath, string identifier)
	{
		try
		{
			return Result.Ok(TimeZoneInfo.FindSystemTimeZoneById(identifier));
		}
		catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			return Fail<TimeZoneInfo>(
				filePath,
				ConnectionFileProblem.UnknownTimeZone,
				$"'{identifier}' is not a time zone this machine knows",
				exception);
		}
	}

	private static Result<TValue> Fail<TValue>(
		string filePath,
		ConnectionFileProblem kind,
		string? reason = null,
		Exception? cause = null)
	{
		var error = reason is null
			? new ConnectionFileError(filePath, kind)
			: new ConnectionFileError(filePath, kind, reason);

		return Result.Fail<TValue>(cause is null ? error : error.CausedBy(new ExceptionalError(cause)));
	}

	private static Result Invalid(
		string filePath,
		ConnectionFileProblem kind,
		IReadOnlyCollection<string> fieldNames,
		string complaint)
	{
		var names = string.Join("', '", fieldNames);
		var subject = fieldNames.Count == 1 ? "field" : "fields";
		var verb = fieldNames.Count == 1 ? "is" : "are";

		return Result.Fail(
			new ConnectionFileError(filePath, kind, $"the required {subject} '{names}' {verb} {complaint}"));
	}
}
