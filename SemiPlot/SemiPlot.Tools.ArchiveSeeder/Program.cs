using FluentResults;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class Program
{
	public static async Task<int> Main(string[] arguments)
	{
		return FollowOptions.IsRequested(arguments)
			? await RunFollowAsync(arguments)
			: await RunSeedAsync(arguments);
	}

	private static async Task<int> RunSeedAsync(string[] arguments)
	{
		var parsed = SeederOptions.Parse(arguments);

		return parsed.IsFailed
			? ReportRejection(parsed, SeederOptions.Usage)
			: await SeedAsync(parsed.Value);
	}

	private static async Task<int> RunFollowAsync(string[] arguments)
	{
		var parsed = FollowOptions.Parse(arguments);

		return parsed.IsFailed
			? ReportRejection(parsed, FollowOptions.Usage)
			: await FollowAsync(parsed.Value);
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

	// The demo writer. It appends to an archive somebody else seeded, so it creates no archive table,
	// plants no break and fills no tag catalogue — only the day partition each tick's rows land in. Each
	// tick appends the raw rows of the span since the previous one and then thins them into the coarse
	// layers, so what it moves is every layer's live edge rather than the raw layer's alone, which is what
	// a wide window's tail reads.
	private static async Task<int> FollowAsync(FollowOptions options)
	{
		using var stopping = new CancellationTokenSource();

		// Ctrl+C stops the loop where it waits rather than tearing the process down inside a COPY: the
		// in-flight append is never handed the token.
		Console.CancelKeyPress += (_, pressed) =>
		{
			pressed.Cancel = true;
			stopping.Cancel();
		};

		ReportFollowPlan(options);

		// Read once, before anything is written, and it answers with the max(t) the loop starts from.
		var freshness = await StaleArchiveGuard.CheckAsync(options.ConnectionString, LocalNow());

		if (freshness.IsFailed)
		{
			ReportErrors(freshness);

			return 1;
		}

		var writer = new ArchiveWriter(options.ConnectionString);

		// Just past the archive's own newest row, so the first tick continues the fill rather than starting
		// a second run of rows a hole away from it, and rather than rewriting the edge row a previous
		// follow run left on the lattice. StaleArchiveGuard has already refused anything further behind the
		// clock than its MaximumAge, so that first tick spans at most the bound, whatever the bound is set
		// to.
		var lastEmitted = StaleArchiveGuard.StartFrom(freshness.Value, LocalNow());

		while (await WaitForTickAsync(options.Interval, stopping.Token))
		{
			var now = LocalNow();
			var appended = await AppendAsync(writer, options, lastEmitted, now);

			if (appended.IsFailed)
			{
				ReportErrors(appended);

				return 1;
			}

			// After the append has committed, never before it: at a cadence longer than a period the
			// tick's own rows are the closing period's last ones, and a flush ahead of them would pick a
			// coarse 'last' row the period had not produced yet.
			var thinned = await CoarseFlush.FlushAsync(options, lastEmitted, now);

			if (thinned.IsFailed)
			{
				ReportErrors(thinned);

				return 1;
			}

			Console.WriteLine(
				$"appended        {appended.Value} rows, {thinned.Value} coarse, up to {now:O}");
			lastEmitted = now;
		}

		Console.WriteLine("stopped");

		return 0;
	}

	// The archive column is 'timestamp(3) without time zone' holding the SCADA host's naive local time
	// (docs/architecture/scada-archive.md#time-semantics), so the follow edge is this machine's local
	// clock with its Kind stripped. DateTime.UtcNow would place the demo's live edge one zone offset from
	// where the viewer, converting through source_time_zone, looks for it.
	private static DateTime LocalNow()
	{
		return DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
	}

	private static async Task<bool> WaitForTickAsync(TimeSpan interval, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(interval, cancellationToken);

			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}

	private static async Task<Result<long>> AppendAsync(
		ArchiveWriter writer,
		FollowOptions options,
		DateTime from,
		DateTime toExclusive)
	{
		var rows = LiveTailGenerator.Generate(options, from, toExclusive);

		if (rows.Count == 0)
		{
			return Result.Ok(0L);
		}

		return await writer.WriteAsync(rows, from, toExclusive, allowExistingRows: true);
	}

	private static void ReportFollowPlan(FollowOptions options)
	{
		Console.WriteLine($"mode            follow, every {options.Interval.TotalSeconds:0.###} s");
		Console.WriteLine($"pens            {options.PenCount}");
		Console.WriteLine($"seed            {options.Seed}");
		Console.WriteLine($"change seconds  {options.ChangeSeconds}");
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

	private static int ReportRejection(ResultBase result, string usage)
	{
		ReportErrors(result);
		Console.Error.WriteLine();
		Console.Error.WriteLine(usage);

		return 1;
	}

	private static void ReportErrors(ResultBase result)
	{
		foreach (var error in result.Errors)
		{
			Console.Error.WriteLine(error.Message);
		}
	}
}
