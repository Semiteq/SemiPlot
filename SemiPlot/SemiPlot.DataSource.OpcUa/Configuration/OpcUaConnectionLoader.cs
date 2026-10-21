using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.OpcUa.Configuration;

/// <summary>Every member nullable so an absent field reaches the loader as a state to report.</summary>
internal sealed class OpcUaConnectionDto
{
	public string? Host { get; set; }

	public int? Port { get; set; }
}

/// <summary>
/// Reads the <c>opc/</c> section folder into <see cref="OpcUaConnectionSettings"/>. Every failure is a
/// <see cref="ConnectionFileError"/> or a <see cref="ConfigurationSectionError"/> of the <c>Opc</c> section in
/// the result; nothing escapes as an exception, and keys the format does not name are ignored.
/// </summary>
public static class OpcUaConnectionLoader
{
	public const string HostKey = ConnectionFile.HostKey;

	public const string PortKey = ConnectionFile.PortKey;

	public const string CertificateStoreDirectoryName = "pki";

	private const ConfigurationSectionName Section = ConfigurationSectionName.Opc;

	public static Result<OpcUaConnectionSettings> Load(string sectionDirectory)
	{
		var section = ConfigurationSection.Read(sectionDirectory, Section);

		if (section.IsFailed)
		{
			return Result.Fail<OpcUaConnectionSettings>(section.Errors);
		}

		var read = ConnectionFile.Deserialize<OpcUaConnectionDto>(Section, sectionDirectory, section.Value);

		if (read.IsFailed)
		{
			return Result.Fail<OpcUaConnectionSettings>(read.Errors);
		}

		var dto = read.Value;
		var checks = ConnectionFile.Validate(Section, sectionDirectory, dto.Host, dto.Port, []);

		if (checks.IsFailed)
		{
			return Result.Fail<OpcUaConnectionSettings>(checks.Errors);
		}

		return Result.Ok(new OpcUaConnectionSettings(
			dto.Host!,
			dto.Port!.Value,
			Path.Combine(sectionDirectory, CertificateStoreDirectoryName)));
	}
}
