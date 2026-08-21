using FluentResults;

using SemiPlot.Core.Data.Errors;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// Reads the archive connection file into <see cref="PostgresConnectionSettings"/>. Every failure is a
/// typed error in the returned <see cref="Result{TValue}"/>; nothing escapes as an exception, for any
/// input including a blank path, so a malformed file is reported at startup rather than at the first
/// query.
/// </summary>
public static class PostgresConnectionLoader
{
	private const string SupportedFileVersion = "1.0";

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
			return Result.Fail<PostgresConnectionSettings>(new ConnectionFileNotFoundError(filePath));
		}

		var read = Read(filePath);

		if (read.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(read.Errors);
		}

		var dto = read.Value;

		// The version is checked ahead of the fields, because a file written for another version is
		// allowed to lack fields this one requires and the version is the answer the operator can act on.
		var version = ValidateVersion(filePath, dto.ConnectionFileVersion);

		if (version.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(version.Errors);
		}

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
			return Result.Fail<PostgresConnectionDto>(
				new ConnectionFileNotFoundError(filePath).CausedBy(new ExceptionalError(exception)));
		}
		catch (Exception exception)
		{
			return Result.Fail<PostgresConnectionDto>(
				new ConnectionFileInvalidError(
						filePath, ConnectionFileProblem.Unreadable, "the file cannot be opened for reading")
					.CausedBy(new ExceptionalError(exception)));
		}

		try
		{
			var dto = _deserializer.Deserialize<PostgresConnectionDto?>(content);

			return dto is null
				? Result.Fail<PostgresConnectionDto>(
					new ConnectionFileInvalidError(
						filePath, ConnectionFileProblem.Unparseable, "the file carries no configuration"))
				: Result.Ok(dto);
		}
		catch (Exception exception)
		{
			return Result.Fail<PostgresConnectionDto>(
				new ConnectionFileInvalidError(
						filePath, ConnectionFileProblem.Unparseable, "the file is not valid YAML")
					.CausedBy(new ExceptionalError(exception)));
		}
	}

	/// <summary>
	/// Reports a file written for another version ahead of the fields that version does not carry. The
	/// guard reaches absent fields only: <see cref="Read"/> deserializes the whole document first, so a
	/// later file version that changes a key's YAML type is reported as
	/// <see cref="ConnectionFileProblem.Unparseable"/> and the operator never learns the version is the
	/// real problem. A version bump that must stay reportable adds keys rather than retyping them.
	/// </summary>
	private static Result ValidateVersion(string filePath, string? foundVersion)
	{
		if (string.IsNullOrWhiteSpace(foundVersion))
		{
			return Invalid(
				filePath, ConnectionFileProblem.MissingField, ["connection_file_version"], "absent or blank");
		}

		if (!string.Equals(foundVersion, SupportedFileVersion, StringComparison.Ordinal))
		{
			return Result.Fail(
				new ConnectionFileInvalidError(
					filePath,
					ConnectionFileProblem.VersionMismatch,
					$"the file is version '{foundVersion}', not the supported '{SupportedFileVersion}'"));
		}

		return Result.Ok();
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

		(string Name, int? Value)[] numbers =
		[
			(PortKey, dto.Port),
			(PollIntervalKey, dto.PollIntervalMs)
		];

		var missing = texts.Where(pair => string.IsNullOrWhiteSpace(pair.Value)).Select(pair => pair.Name)
			.Concat(numbers.Where(pair => pair.Value is null).Select(pair => pair.Name))
			.ToArray();

		return missing.Length == 0
			? Result.Ok()
			: Invalid(filePath, ConnectionFileProblem.MissingField, missing, "absent or blank");
	}

	// A port of 0 and a negative interval parse as integers and then detonate downstream — the Npgsql
	// builder rejects a port outside 1..65535 on assignment, and a non-positive interval becomes a
	// TimeSpan nothing checks. They belong in the loader's Result like every other file fault.
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
			return Result.Fail<TimeZoneInfo>(
				new ConnectionFileInvalidError(
						filePath,
						ConnectionFileProblem.UnknownTimeZone,
						$"'{identifier}' is not a time zone this machine knows")
					.CausedBy(new ExceptionalError(exception)));
		}
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
			new ConnectionFileInvalidError(filePath, kind, $"the required {subject} '{names}' {verb} {complaint}"));
	}
}
