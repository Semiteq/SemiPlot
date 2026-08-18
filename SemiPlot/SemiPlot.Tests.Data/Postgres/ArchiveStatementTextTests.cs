using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// Pins the provider's statement text against the architecture document itself, read at run time, so that
// editing one side alone fails here. A literal copied into this file would only catch an edit to the
// code, which is the half nobody needs pinning.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatementTextTests
{
	private const string DocumentPath = "docs/architecture/data-integration.md";

	[Theory]
	[InlineData("### Pen catalog", ArchiveStatements.PenCatalog)]
	[InlineData("### Archive extent", ArchiveStatements.ArchiveExtent)]
	public void EachDocumentedStatementMatchesTheConstantCharacterForCharacter(string heading, string statement)
	{
		var documented = ExtractFencedSql(ReadDocument(DocumentPath), heading);

		Assert.Equal(documented, Normalise(statement));
	}

	[Fact]
	public void AMissingDocumentFailsRatherThanPassingSilently()
	{
		Assert.Throws<FileNotFoundException>(() => ReadDocument("docs/architecture/no-such-document.md"));
	}

	[Fact]
	public void AMissingHeadingFailsRatherThanPassingSilently()
	{
		var document = "### Something else\n\n```sql\nSELECT 1;\n```\n";

		Assert.Throws<InvalidOperationException>(() => ExtractFencedSql(document, "### Pen catalog"));
	}

	[Fact]
	public void AHeadingWithNoFenceFailsRatherThanPassingSilently()
	{
		var document = "### Pen catalog\n\nprose only, no fence\n\n### Next\n";

		Assert.Throws<InvalidOperationException>(() => ExtractFencedSql(document, "### Pen catalog"));
	}

	[Fact]
	public void AnUnclosedFenceFailsRatherThanPassingSilently()
	{
		var document = "### Pen catalog\n\n```sql\nSELECT 1;\n";

		Assert.Throws<InvalidOperationException>(() => ExtractFencedSql(document, "### Pen catalog"));
	}

	private static string ReadDocument(string relativePath)
	{
		var path = Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

		if (!File.Exists(path))
		{
			throw new FileNotFoundException("The architecture document is missing from the repository.", path);
		}

		return File.ReadAllText(path);
	}

	private static string FindRepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "SemiPlot.slnx")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			$"No SemiPlot.slnx found above {AppContext.BaseDirectory}, so {DocumentPath} cannot be located.");
	}

	private static string ExtractFencedSql(string document, string heading)
	{
		var lines = Normalise(document).Split('\n');
		var headingIndex = Array.IndexOf(lines, heading);

		if (headingIndex < 0)
		{
			throw new InvalidOperationException($"Heading '{heading}' is absent from {DocumentPath}.");
		}

		var openIndex = FindOpeningFence(lines, headingIndex);

		if (openIndex < 0)
		{
			throw new InvalidOperationException($"No fenced sql block follows '{heading}' in {DocumentPath}.");
		}

		var closeIndex = Array.IndexOf(lines, "```", openIndex + 1);

		if (closeIndex < 0)
		{
			throw new InvalidOperationException(
				$"The fenced sql block under '{heading}' in {DocumentPath} is never closed.");
		}

		return string.Join('\n', lines[(openIndex + 1)..closeIndex]);
	}

	// Bounded by the next heading, so a section carrying no fence reports itself instead of borrowing the
	// following section's block.
	private static int FindOpeningFence(string[] lines, int headingIndex)
	{
		for (var index = headingIndex + 1; index < lines.Length; index++)
		{
			if (lines[index].StartsWith('#'))
			{
				return -1;
			}

			if (lines[index] == "```sql")
			{
				return index;
			}
		}

		return -1;
	}

	private static string Normalise(string text)
	{
		return text.Replace("\r\n", "\n");
	}
}
