using Microsoft.Extensions.Logging;

using Npgsql;

namespace SemiPlot.Tools.ArchiveSeeder;

public static class Program
{
	private static readonly string _opcCertificateDirectory =
		Path.Combine(Path.GetTempPath(), "SemiPlot", "OpcDemoServer");

	public static Task<int> Main(string[] arguments)
	{
		return SeederCommand.RunAsync(arguments, options => ReportingAsync(() => SeedAsync(options)),
			options => ReportingAsync(() => FollowAsync(options)),
			options => ReportingAsync(() => ConvergeAsync(options)));
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
		var fill = await SeedFiller.FillAsync(options);

		ReportPlan(options, fill.Rows);

		Console.WriteLine($"rows written    {fill.RowsWritten}");
		Console.WriteLine(fill.TagsWritten is { } tags
			? $"tags written    {tags}"
			: "tags written    skipped, no --admin-connection");

		return 0;
	}

	// docs/architecture/bench.md#the-demo-writer
	private static async Task<int> FollowAsync(FollowOptions options)
	{
		// Disposed at method exit, after the server's explicit dispose, so its queue flushes the server's last lines.
		using var loggerFactory = CreateServerLoggerFactory();
		var stopping = new CancellationTokenSource();

		// Ctrl+C stops the loop where it waits rather than tearing the process down inside a COPY: the
		// in-flight append is never handed the token.
		Console.CancelKeyPress += StopOnCancel;

		// Read once, before anything is written: the edge the first tick continues from.
		var newest = await StaleArchiveGuard.CheckAsync(options.ConnectionString, ArchiveClock(options));

		// Open at the edge: the edge row already sits on the lattice this run walks again.
		var edge = new FollowEdge(newest ?? ArchiveClock(options));
		var writer = new ArchiveWriter(options.ConnectionString);

		var server = await DemoScadaServer.StartAsync(
			options, TimeZoneInfo.Local, _opcCertificateDirectory, loggerFactory);

		try
		{
			ReportFollowPlan(options, server);
			await AppendUntilStoppedAsync(writer, options, edge, server, stopping.Token);
		}
		catch (Exception primary)
		{
			await DisposeBesideAsync(server, primary, loggerFactory.CreateLogger<DemoScadaServer>());

			throw;
		}

		await server.DisposeAsync();
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

	private static async Task AppendUntilStoppedAsync(
		ArchiveWriter writer,
		FollowOptions options,
		FollowEdge edge,
		DemoScadaServer server,
		CancellationToken cancellationToken)
	{
		while (await WaitForTickAsync(options.Interval, cancellationToken))
		{
			await ThrowIfServerEndedAsync(server.Completion);

			if (edge.Advance(ArchiveClock(options)) is { } window)
			{
				await AppendAndFlushAsync(writer, options, window.After, window.To);
			}
		}

		// A graceful stop writes the span the lag held back.
		if (edge.Advance(LocalNow()) is { } heldBack)
		{
			await AppendAndFlushAsync(writer, options, heldBack.After, heldBack.To);
		}
	}

	private static async Task DisposeBesideAsync(DemoScadaServer server, Exception primary, ILogger logger)
	{
		try
		{
			await server.DisposeAsync();
		}
		catch (Exception disposal)
		{
			// The dispose rethrows a publishing-loop fault as the same object the tick loop already threw.
			if (!ReferenceEquals(disposal, primary))
			{
				logger.LogError(disposal, "The OPC UA demo server failed to stop");
			}
		}
	}

	// The archive column is 'timestamp(3) without time zone' holding the SCADA host's naive local time
	// (docs/architecture/scada-archive.md#time-semantics).
	private static DateTime LocalNow()
	{
		return DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
	}

	private static DateTime ArchiveClock(FollowOptions options)
	{
		return LocalNow() - options.ArchiveLag;
	}

	// No configuration source, so the console-formatter variables the AppHost injects stay inert.
	private static ILoggerFactory CreateServerLoggerFactory()
	{
		return LoggerFactory.Create(builder => builder
			.SetMinimumLevel(LogLevel.Warning)
			.AddSimpleConsole(console => console.SingleLine = true)
			.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Warning));
	}

	private static async Task ThrowIfServerEndedAsync(Task completion)
	{
		if (!completion.IsCompleted)
		{
			return;
		}

		await completion;

		throw new InvalidOperationException("The OPC UA publishing loop ended.");
	}

	private static async Task AppendAndFlushAsync(
		ArchiveWriter writer,
		FollowOptions options,
		DateTime after,
		DateTime to)
	{
		var appended = await AppendAsync(writer, options, after, to);

		// After the append has committed, never before it: at a cadence longer than a period the tick's own
		// rows are the closing period's last ones, and a flush ahead of them would pick a coarse 'last' row
		// the period had not produced yet.
		var thinned = await CoarseFlush.FlushAsync(options, after, to);

		Console.WriteLine($"appended        {appended} rows, {thinned} coarse, up to {to:O}");
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

	private static void ReportFollowPlan(FollowOptions options, DemoScadaServer server)
	{
		Console.WriteLine($"mode            follow, every {options.Interval.TotalSeconds:0.###} s");
		Console.WriteLine($"pens            {options.PenCount}");
		Console.WriteLine($"seed            {options.Seed}");
		Console.WriteLine($"change seconds  {options.ChangeSeconds}");
		Console.WriteLine($"archive lag     {options.ArchiveLag.TotalSeconds:0.###} s");
		Console.WriteLine($"opc             {server.EndpointUrl}, {options.PenCount} variables");
	}

	private static async Task<int> ConvergeAsync(ConvergeOptions options)
	{
		var result = await Converge.RunAsync(options);

		Console.WriteLine($"database        {result.Database}, ready in {result.ReadinessWait.TotalSeconds:0.#} s");
		Console.WriteLine($"rows written    {result.RowsWritten}");
		Console.WriteLine($"tags written    {result.TagsWritten}");
		Console.WriteLine($"config          {ConnectionFileWriter.PathFor(options.ConfigDirectory)}");

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
}
