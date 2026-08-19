using System.Text.RegularExpressions;

using Npgsql;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// Pins the provider's statement text against the architecture document itself, read at run time, so that
// editing one side alone fails here. A literal copied into this file would catch the code half only, and
// it is the weaker guard rather than the cheaper one: the document is the artifact each slice's brief is
// assembled from, so a fence that silently stops describing the shipped statement corrupts the next
// slice's plan while every test stays green.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatementTextTests
{
	private const string DocumentPath = "docs/architecture/data-integration.md";

	// Npgsql strips the sigil, so the command carries "ids" where the statement carries "@ids".
	private static readonly Regex _parameterTokenPattern = new(@"@(\w+)");

	[Theory]
	[InlineData("### Pen catalog", ArchiveStatements.PenCatalog)]
	[InlineData("### Archive extent", ArchiveStatements.ArchiveExtent)]
	[InlineData("### History, chosen layer already sparse enough", ArchiveStatements.SparseHistoryWindow)]
	public void EachDocumentedStatementMatchesTheConstantCharacterForCharacter(string heading, string statement)
	{
		var documented = ExtractFencedSql(ReadDocument(DocumentPath), heading);

		Assert.Equal(documented, Normalise(statement));
	}

	// The drift that breaks production is the binder naming a parameter the statement does not.
	[Fact]
	public void TheWindowBinderNamesExactlyTheStatementsOwnParameters()
	{
		using var command = new NpgsqlCommand(ArchiveStatements.SparseHistoryWindow);

		PostgresDataProvider.BindWindow(
			command,
			new ArchiveTimeConverter(TimeZoneInfo.Utc),
			[1, 2],
			new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
			new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc),
			AggregationLayer.Raw);

		var bound = command.Parameters
			.Select(parameter => parameter.ParameterName)
			.Order(StringComparer.Ordinal)
			.ToArray();

		var declared = _parameterTokenPattern.Matches(ArchiveStatements.SparseHistoryWindow)
			.Select(match => match.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		Assert.NotEmpty(declared);
		Assert.Equal(declared, bound);
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
