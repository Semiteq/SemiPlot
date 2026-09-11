using FluentResults;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SemiPlot.UI.Startup;

/// <summary>Both members nullable so an absent key reaches the loader as a state to report.</summary>
internal sealed class AppSettingsDto
{
	public string? Locale { get; set; }

	public string? Theme { get; set; }
}

/// <summary>
/// Reads the interface settings file into <see cref="AppSettings"/>. Every failure is an
/// <see cref="AppSettingsError"/> in the returned <see cref="Result{TValue}"/>; nothing escapes as an
/// exception, for any input including a blank path. Keys the format does not name are ignored.
/// </summary>
public static class AppSettingsLoader
{
	internal const string LocaleKey = "locale";

	internal const string ThemeKey = "theme";

	private static readonly (string Text, UiLanguage Value)[] _languages =
		[.. SettingsVocabulary.Languages.Select(entry => (Text: entry.YamlToken, Value: entry.Language))];

	private static readonly (string Text, AppThemeVariant Value)[] _themes =
		[.. SettingsVocabulary.Themes.Select(entry => (Text: entry.YamlToken, Value: entry.Theme))];

	private static readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.IgnoreUnmatchedProperties()
		.Build();

	public static Result<AppSettings> Load(string filePath)
	{
		var read = Read(filePath);

		if (read.IsFailed)
		{
			return Result.Fail<AppSettings>(read.Errors);
		}

		var locale = ParseKey(filePath, LocaleKey, read.Value.Locale, _languages);

		if (locale.IsFailed)
		{
			return Result.Fail<AppSettings>(locale.Errors);
		}

		var theme = ParseKey(filePath, ThemeKey, read.Value.Theme, _themes);

		if (theme.IsFailed)
		{
			return Result.Fail<AppSettings>(theme.Errors);
		}

		return Result.Ok(new AppSettings(locale.Value, theme.Value));
	}

	// An empty document parses to no DTO, which is every key absent rather than an unreadable file.
	private static Result<AppSettingsDto> Read(string filePath)
	{
		string content;

		try
		{
			content = File.ReadAllText(filePath);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			return Fail<AppSettingsDto>(filePath, AppSettingsProblem.NotFound, cause: exception);
		}
		catch (Exception exception)
		{
			return Fail<AppSettingsDto>(filePath, AppSettingsProblem.Unreadable, cause: exception);
		}

		try
		{
			return Result.Ok(_deserializer.Deserialize<AppSettingsDto?>(content) ?? new AppSettingsDto());
		}
		catch (Exception exception)
		{
			return Fail<AppSettingsDto>(filePath, AppSettingsProblem.Unreadable, cause: exception);
		}
	}

	private static Result<TValue> ParseKey<TValue>(
		string filePath,
		string key,
		string? value,
		(string Text, TValue Value)[] accepted)
		where TValue : struct
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Fail<TValue>(filePath, AppSettingsProblem.KeyMissing, key);
		}

		foreach (var (text, parsed) in accepted)
		{
			if (string.Equals(text, value.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return Result.Ok(parsed);
			}
		}

		return Fail<TValue>(filePath, AppSettingsProblem.ValueInvalid, key, Describe(accepted));
	}

	private static string Describe<TValue>((string Text, TValue Value)[] accepted)
	{
		return string.Join(", ", accepted.Select(pair => pair.Text));
	}

	private static Result<TValue> Fail<TValue>(
		string filePath,
		AppSettingsProblem kind,
		string key = "",
		string acceptedValues = "",
		Exception? cause = null)
	{
		var error = new AppSettingsError(filePath, kind, key, acceptedValues);

		return Result.Fail<TValue>(cause is null ? error : error.CausedBy(new ExceptionalError(cause)));
	}
}
