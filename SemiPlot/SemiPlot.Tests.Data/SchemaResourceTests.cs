using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// A csproj item that stops matching the schema script would fail here rather than at the first
// database connection.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class SchemaResourceTests
{
	[Fact]
	public void TheSeederAssemblyCarriesTheSchemaScript()
	{
		var names = typeof(ArchiveWriter).Assembly.GetManifestResourceNames();

		Assert.Contains(ArchiveWriter.SchemaResourceName, names);
	}

	// docs/architecture/scada-archive.md#database-objects: the vendor's five columns, the range
	// partitioning, the tpk primary key and the catch-all partition.
	[Theory]
	[InlineData("CREATE TABLE public.trends")]
	[InlineData("id integer DEFAULT 0 NOT NULL")]
	[InlineData("l smallint DEFAULT 0 NOT NULL")]
	[InlineData("t timestamp(3) without time zone NOT NULL")]
	[InlineData("v double precision")]
	[InlineData("q integer NOT NULL")]
	[InlineData("PARTITION BY RANGE (t)")]
	[InlineData("ADD CONSTRAINT tpk PRIMARY KEY (id, l, t)")]
	[InlineData("CREATE TABLE public.tpdefault PARTITION OF public.trends DEFAULT")]
	public void TheSchemaScriptHoldsTheVendorDefinition(string expected)
	{
		Assert.Contains(expected, ArchiveWriter.ReadSchemaScript(), StringComparison.Ordinal);
	}

	// `messages` and the customer's own test table are excluded on purpose: no slice of the PostgreSQL
	// data source reads either, and the day partitions are the seeder's to create per run.
	[Fact]
	public void TheSchemaScriptCreatesNothingBeyondTrendsAndItsDefaultPartition()
	{
		var created = ArchiveWriter.ReadSchemaScript()
			.Split('\n')
			.Select(line => line.TrimStart())
			.Where(line => line.StartsWith("CREATE TABLE", StringComparison.Ordinal))
			.ToArray();

		Assert.Equal(
			["CREATE TABLE public.trends (", "CREATE TABLE public.tpdefault PARTITION OF public.trends DEFAULT;"],
			created.Select(line => line.TrimEnd('\r')));
	}
}
