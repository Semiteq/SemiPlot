using FluentResults;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class Program
{
	public static async Task<int> Main(string[] arguments)
	{
		var parsed = SeederOptions.Parse(arguments);

		if (parsed.IsFailed)
		{
			ReportErrors(parsed);
			Console.Error.WriteLine();
			Console.Error.WriteLine(SeederOptions.Usage);

			return 1;
		}

		return await SeedAsync(parsed.Value);
	}

	private static async Task<int> SeedAsync(SeederOptions options)
	{
		var rawRows = RawLayerGenerator.Generate(options);
		var rows = rawRows.Concat(LayerThinner.ThinAll(rawRows)).ToArray();

		ReportPlan(options, rows);

		var written = await new ArchiveWriter(options.ConnectionString)
			.WriteAsync(rows, options.Start, options.End);

		if (written.IsFailed)
		{
			ReportErrors(written);

			return 1;
		}

		Console.WriteLine($"rows written    {written.Value}");

		return await SeedTagsAsync(options);
	}

	private static async Task<int> SeedTagsAsync(SeederOptions options)
	{
		if (options.AdminConnectionString is null)
		{
			Console.WriteLine("tags written    skipped, no --admin-connection");

			return 0;
		}

		var tags = await new TagCatalogWriter(options.AdminConnectionString)
			.WriteAsync(RawLayerGenerator.SelectPens(options.PenCount));

		if (tags.IsFailed)
		{
			ReportErrors(tags);

			return 1;
		}

		Console.WriteLine($"tags written    {tags.Value}");

		return 0;
	}

	private static void ReportPlan(SeederOptions options, IReadOnlyCollection<ArchiveRow> rows)
	{
		Console.WriteLine($"span            {options.Start:O} .. {options.End:O} (exclusive)");
		Console.WriteLine($"pens            {options.PenCount}");
		Console.WriteLine($"seed            {options.Seed}");
		Console.WriteLine($"change seconds  {options.ChangeSeconds}");
		Console.WriteLine($"breaks          {options.BreakCount}");
		Console.WriteLine($"partitions      {PartitionScript.CoveredDays(options.Start, options.End).Count}");

		foreach (var layer in rows.GroupBy(row => row.Layer).OrderBy(layer => layer.Key))
		{
			Console.WriteLine($"layer {layer.Key} rows    {layer.Count()}");
		}
	}

	private static void ReportErrors(ResultBase result)
	{
		foreach (var error in result.Errors)
		{
			Console.Error.WriteLine(error.Message);
		}
	}
}
