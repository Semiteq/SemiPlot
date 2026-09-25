using FluentResults;

namespace SemiPlot.Core.Configuration;

/// <summary>
/// Stages a section into an existing staging folder: each edited key is written into the file that owns it and
/// every other file is copied byte for byte. Every failure is a <see cref="ConfigurationSectionError"/>.
/// </summary>
public static class ConfigurationSectionWriter
{
	/// <summary>Stages the section and returns the names of the files it rewrote.</summary>
	public static Result<IReadOnlyList<string>> Stage(
		OwnedSection owned,
		IReadOnlyDictionary<string, string> edits,
		ConfigurationSectionName section,
		string sectionDirectory,
		string stagingDirectory)
	{
		var editsByOwner = GroupByOwner(owned, edits, section, sectionDirectory);

		if (editsByOwner.IsFailed)
		{
			return Result.Fail<IReadOnlyList<string>>(editsByOwner.Errors);
		}

		var target = new StagedSection(section, sectionDirectory, stagingDirectory);
		var copied = target.CopyUnedited(editsByOwner.Value.Keys);

		if (copied.IsFailed)
		{
			return Result.Fail<IReadOnlyList<string>>(copied.Errors);
		}

		var rewritten = new List<string>();

		foreach (var (name, fileEdits) in editsByOwner.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			var result = target.Rewrite(name, fileEdits);

			if (result.IsFailed)
			{
				return Result.Fail<IReadOnlyList<string>>(result.Errors);
			}

			rewritten.Add(name);
		}

		return Result.Ok<IReadOnlyList<string>>(rewritten);
	}

	private static Result<Dictionary<string, Dictionary<string, string>>> GroupByOwner(
		OwnedSection owned,
		IReadOnlyDictionary<string, string> edits,
		ConfigurationSectionName section,
		string sectionDirectory)
	{
		var editsByOwner = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

		foreach (var (key, value) in edits)
		{
			if (!owned.Owners.TryGetValue(key, out var owner))
			{
				return ConfigurationSection.Fail(section, sectionDirectory, SectionProblem.KeyAbsent, key)
					.ToResult<Dictionary<string, Dictionary<string, string>>>();
			}

			if (!editsByOwner.TryGetValue(owner, out var fileEdits))
			{
				fileEdits = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				editsByOwner[owner] = fileEdits;
			}

			fileEdits[key] = value;
		}

		return Result.Ok(editsByOwner);
	}

	private sealed class StagedSection(
		ConfigurationSectionName section,
		string sectionDirectory,
		string stagingDirectory)
	{
		public Result CopyUnedited(IEnumerable<string> rewrittenNames)
		{
			var skipped = rewrittenNames.ToHashSet(StringComparer.Ordinal);
			string[] files;

			try
			{
				files = Directory.GetFiles(sectionDirectory, ConfigurationSection.FilePattern);
			}
			catch (Exception exception)
			{
				return ConfigurationSection.Fail(
					section, sectionDirectory, SectionProblem.Unlistable, cause: exception);
			}

			foreach (var file in files)
			{
				var name = Path.GetFileName(file);

				if (skipped.Contains(name))
				{
					continue;
				}

				var copied = Copy(file, name);

				if (copied.IsFailed)
				{
					return copied;
				}
			}

			return Result.Ok();
		}

		// A key is written back under the edit's spelling in the position the file gave it, so the mapping
		// never carries one key in two cases.
		public Result Rewrite(string name, Dictionary<string, string> fileEdits)
		{
			var parsed = ConfigurationSection.ParseFile(
				Path.Combine(sectionDirectory, name), name, section, sectionDirectory);

			if (parsed.IsFailed)
			{
				return parsed.ToResult();
			}

			var mapping = new Dictionary<string, object?>(StringComparer.Ordinal);
			var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach (var (key, value) in parsed.Value)
			{
				var editedKey = fileEdits.Keys.FirstOrDefault(
					edited => string.Equals(edited, key, StringComparison.OrdinalIgnoreCase));

				if (editedKey is null)
				{
					mapping[key] = value;
				}
				else
				{
					mapping[editedKey] = fileEdits[editedKey];
					applied.Add(editedKey);
				}
			}

			var absent = fileEdits.Keys.FirstOrDefault(key => !applied.Contains(key));

			if (absent is not null)
			{
				return ConfigurationSection.Fail(section, sectionDirectory, SectionProblem.KeyAbsent, absent);
			}

			return Write(name, ConfigurationSection.Serializer.Serialize(mapping));
		}

		private Result Copy(string file, string name)
		{
			byte[] content;

			try
			{
				content = File.ReadAllBytes(file);
			}
			catch (Exception exception)
			{
				return ConfigurationSection.Fail(
					section, sectionDirectory, SectionProblem.Unreadable, fileNames: [name], cause: exception);
			}

			try
			{
				File.WriteAllBytes(Path.Combine(stagingDirectory, name), content);

				return Result.Ok();
			}
			catch (Exception exception)
			{
				return ConfigurationSection.Fail(
					section, sectionDirectory, SectionProblem.Unwritable, fileNames: [name], cause: exception);
			}
		}

		private Result Write(string name, string content)
		{
			try
			{
				File.WriteAllText(Path.Combine(stagingDirectory, name), content);

				return Result.Ok();
			}
			catch (Exception exception)
			{
				return ConfigurationSection.Fail(
					section, sectionDirectory, SectionProblem.Unwritable, fileNames: [name], cause: exception);
			}
		}
	}
}
