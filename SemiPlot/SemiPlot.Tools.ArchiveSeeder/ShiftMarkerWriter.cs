using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;

namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>A marker of a shifts seed in the archive's wall-clock time: a locked recipe marker, or an operator's
/// unlocked one whose label may be absent.</summary>
public readonly record struct SeededMarker(DateTime Time, string? Label, MarkerKind Kind, bool IsLocked);

// Connects as scada_writer, the role the SCADA writes its recipe markers as. The role holds INSERT on
// semiplot_markers and nothing else, so the write reads nothing back (docs/architecture/postgres-instance.md).
public sealed class ShiftMarkerWriter(string connectionString, TimeZoneInfo archiveZone)
{
	private const string CopyCommand =
		"COPY public.semiplot_markers (t, label, kind, locked) FROM STDIN (FORMAT BINARY)";

	private const string OperatorLabelsResource = "OperatorMarkerLabels.txt";

	private const double ThreeMarkerWeekShare = 0.5;
	private const double UnlabelledShare = 0.15;

	// Hash coordinates clear of ShiftSchedule's day draws (1 << 40) and its maintenance phase (1 << 41).
	private const long OperatorCoordinates = 1L << 42;
	private const long DrawsPerDay = 8;
	private const int WeekCountDraw = 0;
	private const int DayOrderDraw = 1;
	private const int TimeDraw = 2;
	private const int LabelPresenceDraw = 3;
	private const int LabelDraw = 4;

	private static readonly string[] _recipeNames = ["Etch.csv", "Ash.csv", "Deposit.csv", "Clean.csv", "Season.csv"];

	private static readonly string[] _operatorLabels = ReadOperatorLabels();

	/// <summary>A start and a closing marker for every run of a shifts seed that opens in the span and closes
	/// before its end.</summary>
	public static IReadOnlyList<SeededMarker> RecipePlan(SeederOptions options)
	{
		if (options.Schedule != ArchiveSchedule.Shifts)
		{
			return [];
		}

		var intervalTicks = RawLayerGenerator.ChangeIntervalTicks(options.ChangeSeconds);
		var schedule = ShiftSchedule.For(options);
		var span = schedule.Clip(options.Start, options.End);
		var markers = new List<SeededMarker>();

		for (var day = DateOnly.FromDateTime(span.Start); day <= DateOnly.FromDateTime(span.End); day = day.AddDays(1))
		{
			foreach (var run in schedule.RecipeRuns(day))
			{
				if (run.Start < span.Start || run.End >= span.End)
				{
					continue;
				}

				var label = _recipeNames[run.Start.Ticks / intervalTicks % _recipeNames.Length];
				var closing = run.IsInterrupted ? MarkerKind.RecipeInterrupted : MarkerKind.RecipeEnd;

				markers.Add(new SeededMarker(run.Start, label, MarkerKind.RecipeStart, IsLocked: true));
				markers.Add(new SeededMarker(run.End, label, closing, IsLocked: true));
			}
		}

		return markers;
	}

	/// <summary>Two or three operator markers per working week of a shifts seed, each inside the session of a day
	/// of its own, on a whole second of the span.</summary>
	public static IReadOnlyList<SeededMarker> OperatorPlan(SeederOptions options)
	{
		if (options.Schedule != ArchiveSchedule.Shifts)
		{
			return [];
		}

		var schedule = ShiftSchedule.For(options);
		var span = schedule.Clip(options.Start, options.End);
		var lastMonday = Monday(DateOnly.FromDateTime(span.End));
		var markers = new List<SeededMarker>();

		for (var monday = Monday(DateOnly.FromDateTime(span.Start)); monday <= lastMonday; monday = monday.AddDays(7))
		{
			foreach (var marker in WeekMarkers(options.Seed, schedule, monday))
			{
				if (marker.Time >= span.Start && marker.Time < span.End)
				{
					markers.Add(marker);
				}
			}
		}

		return markers;
	}

	/// <summary>One binary COPY of the markers; each moment is bound as timestamptz through the archive zone.</summary>
	public async Task WriteAsync(
		IReadOnlyList<SeededMarker> markers,
		CancellationToken cancellationToken = default)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var writer = await connection.BeginBinaryImportAsync(CopyCommand, cancellationToken);

		foreach (var marker in markers)
		{
			await writer.StartRowAsync(cancellationToken);
			await writer.WriteAsync(
				ArchiveClock.ToUtc(marker.Time, archiveZone),
				NpgsqlDbType.TimestampTz,
				cancellationToken);

			if (marker.Label is { } label)
			{
				await writer.WriteAsync(label, NpgsqlDbType.Text, cancellationToken);
			}
			else
			{
				await writer.WriteNullAsync(cancellationToken);
			}

			await writer.WriteAsync((short)marker.Kind, NpgsqlDbType.Smallint, cancellationToken);
			await writer.WriteAsync(marker.IsLocked, NpgsqlDbType.Boolean, cancellationToken);
		}

		await writer.CompleteAsync(cancellationToken);
	}

	// Drawn from the week and the calendar alone, so a span that ends later adds markers and moves none.
	private static IEnumerable<SeededMarker> WeekMarkers(long seed, ShiftSchedule schedule, DateOnly monday)
	{
		var count = Draw(seed, monday, WeekCountDraw) < ThreeMarkerWeekShare ? 2 : 3;
		var sessions = new List<(DateOnly Day, BreakPlan.Window Session)>();

		for (var day = monday; day < monday.AddDays(7); day = day.AddDays(1))
		{
			if (schedule.Session(day) is { } session)
			{
				sessions.Add((day, session));
			}
		}

		var chosen = sessions
			.OrderBy(entry => Draw(seed, entry.Day, DayOrderDraw))
			.Take(count)
			.OrderBy(entry => entry.Day);

		foreach (var (day, session) in chosen)
		{
			yield return OperatorMarker(seed, day, session);
		}
	}

	private static SeededMarker OperatorMarker(long seed, DateOnly day, BreakPlan.Window session)
	{
		var earliest = new DateTime(
			(session.Start.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
		var offset = Math.Floor(Draw(seed, day, TimeDraw) * (session.End - earliest).TotalSeconds);
		var label = Draw(seed, day, LabelPresenceDraw) < UnlabelledShare
			? null
			: _operatorLabels[(int)(Draw(seed, day, LabelDraw) * _operatorLabels.Length)];

		return new SeededMarker(earliest + TimeSpan.FromSeconds(offset), label, MarkerKind.User, IsLocked: false);
	}

	// DayNumber zero is a Monday.
	private static DateOnly Monday(DateOnly day)
	{
		return DateOnly.FromDayNumber(day.DayNumber / 7 * 7);
	}

	private static double Draw(long seed, DateOnly day, int slot)
	{
		return SyntheticValueWalk.Fraction(seed, OperatorCoordinates + (day.DayNumber * DrawsPerDay) + slot);
	}

	// The labels are Russian and the C# source stays ASCII (docs/architecture/ui-text.md), so they ship as a
	// resource of this assembly.
	private static string[] ReadOperatorLabels()
	{
		using var stream = typeof(ShiftMarkerWriter).Assembly.GetManifestResourceStream(OperatorLabelsResource)
			?? throw new InvalidOperationException($"The seeder carries no {OperatorLabelsResource} resource.");
		using var reader = new StreamReader(stream);

		var labels = new List<string>();

		while (reader.ReadLine() is { } line)
		{
			if (line.Length > 0)
			{
				labels.Add(line);
			}
		}

		return [.. labels];
	}
}
