using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class Program
{
	public static Task<int> Main(string[] arguments)
	{
		return SeederCommand.RunAsync(arguments, options => ReportingAsync(() => SeedAsync(options)),
			options => ReportingAsync(() => FollowAsync(options)));
	}

	// A refusal, a server answer or a malformed connection string is the operator's to fix and is printed as
	// one line. Anything else is a fault in this tool and keeps its stack trace.
	private static async Task<int> ReportingAsync(Func<Task<int>> run)
	{
		try
		{
			return await run();
		}
		catch (Exception exception) when (exception is SeederException or NpgsqlException or ArgumentException
											  or FormatException)
		{
			Console.Error.WriteLine(exception.Message);

			return 1;
		}
	}

	private static async Task<int> SeedAsync(SeederOptions options)
	{
		var rawRows = RawLayerGenerator.Generate(options);
		ArchiveRow[] rows = [.. rawRows, .. LayerThinner.ThinAll(rawRows)];

		ReportPlan(options, rows);

		var written = await new ArchiveWriter(options.ConnectionString).WriteAsync(rows, options.Start, options.End);

		Console.WriteLine($"rows written    {written}");

		if (options.AdminConnectionString is null)
		{
			Console.WriteLine("tags written    skipped, no --admin-connection");

			return 0;
		}

		var tags = await new TagCatalogWriter(options.AdminConnectionString)
			.WriteAsync(RawLayerGenerator.SelectPens(options.PenCount));

		Console.WriteLine($"tags written    {tags}");

		return 0;
	}

	// The demo writer: each tick appends the span since the previous tick and thins it into the coarse layers.
	private static async Task<int> FollowAsync(FollowOptions options)
	{
		var stopping = new CancellationTokenSource();

		// Ctrl+C stops the loop where it waits rather than tearing the process down inside a COPY: the
		// in-flight append is never handed the token.
		Console.CancelKeyPress += StopOnCancel;

		ReportFollowPlan(options);

		// Read once, before anything is written: the edge the first tick continues from.
		var newest = await StaleArchiveGuard.CheckAsync(options.ConnectionString, LocalNow());

		var writer = new ArchiveWriter(options.ConnectionString);

		// Open at the edge: the edge row already sits on the lattice this run walks again.
		var edge = newest ?? LocalNow();

		while (await WaitForTickAsync(options.Interval, stopping.Token))
		{
			var now = LocalNow();
			var appended = await AppendAsync(writer, options, edge, now);

			// After the append has committed, never before it: at a cadence longer than a period the tick's
			// own rows are the closing period's last ones, and a flush ahead of them would pick a coarse
			// 'last' row the period had not produced yet.
			var thinned = await CoarseFlush.FlushAsync(options, edge, now);

			Console.WriteLine($"appended        {appended} rows, {thinned} coarse, up to {now:O}");
			edge = now;
		}

		Console.CancelKeyPress -= StopOnCancel;
		stopping.Dispose();
		Console.WriteLine("stopped");

		return 0;

		void StopOnCancel(object? sender, ConsoleCancelEventArgs pressed)
		{
			pressed.Cancel = true;
			stopping.Cancel();
		}
	}

	// The archive column is 'timestamp(3) without time zone' holding the SCADA host's naive local time
	// (docs/architecture/scada-archive.md#time-semantics).
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

	// The window is closed at `to` and the writer's partition walk is open at its end.
	private static async Task<long> AppendAsync(ArchiveWriter writer, FollowOptions options, DateTime after, DateTime to)
	{
		var rows = LiveTailGenerator.Generate(options, after, to);

		return rows.Count == 0
			? 0L
			: await writer.WriteAsync(rows, after, to.AddTicks(1), allowExistingRows: true);
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
}
