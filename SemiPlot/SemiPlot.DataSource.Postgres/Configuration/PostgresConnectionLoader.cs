using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// Every member nullable so an absent field reaches the loader as a state to report.
/// </summary>
internal sealed class PostgresConnectionDto
{
	public string? Host { get; set; }

	public int? Port { get; set; }

	public string? Database { get; set; }

	public string? User { get; set; }

	public string? Password { get; set; }

	public string? Schema { get; set; }
}

/// <summary>
/// Reads the archive connection section folder into <see cref="PostgresConnectionSettings"/>. Every
/// failure is a <see cref="ConnectionFileError"/> or a <see cref="ConfigurationSectionError"/> in the
/// result; nothing escapes as an exception, and keys the format does not name are ignored.
/// </summary>
public static class PostgresConnectionLoader
{
	public const string HostKey = ConnectionFile.HostKey;

	public const string PortKey = ConnectionFile.PortKey;

	public const string DatabaseKey = "database";

	public const string UserKey = "user";

	public const string PasswordKey = "password";

	private const string DefaultSchema = "public";

	private const ConfigurationSectionName Section = ConfigurationSectionName.Connection;

	public static Result<PostgresConnectionSettings> Load(string sectionDirectory)
	{
		var section = ConfigurationSection.Read(sectionDirectory, Section);

		if (section.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(section.Errors);
		}

		var read = ConnectionFile.Deserialize<PostgresConnectionDto>(Section, sectionDirectory, section.Value);

		if (read.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(read.Errors);
		}

		var dto = read.Value;
		var checks = ConnectionFile.Validate(
			Section,
			sectionDirectory,
			dto.Host,
			dto.Port,
			[(DatabaseKey, dto.Database), (UserKey, dto.User), (PasswordKey, dto.Password)]);

		if (checks.IsFailed)
		{
			return Result.Fail<PostgresConnectionSettings>(checks.Errors);
		}

		return Result.Ok(Map(dto));
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
			dto.Schema ?? DefaultSchema);
	}
}
