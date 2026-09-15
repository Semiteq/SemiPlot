using FluentResults;

using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace SemiPlot.Core.Configuration;

/// <summary>
/// Merges every <c>*.yaml</c> of a section folder into one mapping, serialized once for the caller's own
/// typed deserializer. A key carried by two files fails naming both; every failure is a
/// <see cref="ConfigurationSectionError"/> in the result and nothing escapes as an exception.
/// </summary>
public static class ConfigurationSection
{
	private const string FilePattern = "*.yaml";

	private static readonly IDeserializer _deserializer = new DeserializerBuilder()
		.WithDuplicateKeyChecking()
		.Build();

	// Quoting keeps a value that also reads as YAML - null, ~, true, a number, a base-60 time - a string
	// across the round trip, while a typed deserializer still converts the quoted scalar to its own type.
	private static readonly ISerializer _serializer = new SerializerBuilder()
		.WithQuotingNecessaryStrings(true)
		.Build();

	public static Result<string> Read(string sectionDirectory, ConfigurationSectionName section)
	{
		if (string.IsNullOrWhiteSpace(sectionDirectory) || !Directory.Exists(sectionDirectory))
		{
			return Fail<string>(section, sectionDirectory, SectionProblem.DirectoryMissing);
		}

		string[] files;

		try
		{
			files = Directory.GetFiles(sectionDirectory, FilePattern);
		}
		catch (Exception exception)
		{
			return Fail<string>(section, sectionDirectory, SectionProblem.Unlistable, cause: exception);
		}

		if (files.Length == 0)
		{
			return Fail<string>(section, sectionDirectory, SectionProblem.NoFiles);
		}

		Array.Sort(files, StringComparer.Ordinal);

		return Merge(files, section, sectionDirectory);
	}

	// Ownership is case-insensitive because the loaders match their DTO properties case-sensitively: two
	// files spelling one key differently would otherwise merge and lose whichever the DTO does not match.
	private static Result<string> Merge(string[] files, ConfigurationSectionName section, string directory)
	{
		var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
		var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var file in files)
		{
			var name = Path.GetFileName(file);
			var parsed = ParseFile(file, name, section, directory);

			if (parsed.IsFailed)
			{
				return Result.Fail<string>(parsed.Errors);
			}

			foreach (var (key, value) in parsed.Value)
			{
				if (owners.TryGetValue(key, out var owner))
				{
					return Fail<string>(section, directory, SectionProblem.KeyConflict, key, [owner, name]);
				}

				owners[key] = name;
				merged[key] = value;
			}
		}

		return Result.Ok(_serializer.Serialize(merged));
	}

	// An empty file deserializes to nothing, which is no keys rather than an unreadable file.
	private static Result<Dictionary<string, object?>> ParseFile(
		string file,
		string name,
		ConfigurationSectionName section,
		string directory)
	{
		try
		{
			var content = File.ReadAllText(file);
			var repeated = FindRepeatedKey(content);

			if (repeated is not null)
			{
				return Fail<Dictionary<string, object?>>(
					section, directory, SectionProblem.DuplicateKey, repeated, [name]);
			}

			return Result.Ok(_deserializer.Deserialize<Dictionary<string, object?>?>(content) ?? []);
		}
		catch (Exception exception)
		{
			return Fail<Dictionary<string, object?>>(
				section, directory, SectionProblem.Unreadable, fileNames: [name], cause: exception);
		}
	}

	// WithDuplicateKeyChecking refuses the file but names the key only inside its own message, and the
	// operator needs the key, so the top-level mapping is walked for it first.
	private static string? FindRepeatedKey(string content)
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var parser = new Parser(new StringReader(content));

		parser.Consume<StreamStart>();

		if (!parser.TryConsume<DocumentStart>(out _) || !parser.TryConsume<MappingStart>(out _))
		{
			return null;
		}

		while (!parser.TryConsume<MappingEnd>(out _))
		{
			var key = parser.Consume<Scalar>().Value;

			parser.SkipThisAndNestedEvents();

			if (!seen.Add(key))
			{
				return key;
			}
		}

		return null;
	}

	private static Result<TValue> Fail<TValue>(
		ConfigurationSectionName section,
		string directory,
		SectionProblem problem,
		string key = "",
		IReadOnlyList<string>? fileNames = null,
		Exception? cause = null)
	{
		var error = new ConfigurationSectionError(section, directory, problem, key, fileNames);

		return Result.Fail<TValue>(cause is null ? error : error.CausedBy(new ExceptionalError(cause)));
	}
}
