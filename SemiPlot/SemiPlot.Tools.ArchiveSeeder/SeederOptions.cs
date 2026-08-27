using System.Globalization;

using FluentResults;

namespace SemiPlot.Tools.ArchiveSeeder;

public sealed record SeederOptions(
	string ConnectionString,
	DateTime End,
	int Days,
	int PenCount,
	long Seed,
	double ChangeSeconds,
	int BreakCount,
	string? AdminConnectionString)
{
	public const int DefaultDays = 1;
	public const int DefaultPenCount = 8;
	public const long DefaultSeed = 1;
	public const double DefaultChangeSeconds = 5.0;
	public const int DefaultBreakCount = 4;

	private const string ConnectionOption = "connection";
	private const string AdminConnectionOption = "admin-connection";
	private const string DaysOption = "days";
	private const string PensOption = "pens";
	private const string SeedOption = "seed";
	private const string ChangeSecondsOption = "change-seconds";
	private const string BreakCountOption = "break-count";
	private const string EndOption = "end";

	private static readonly string[] _knownOptions =
	[
		ConnectionOption,
		AdminConnectionOption,
		DaysOption,
		PensOption,
		SeedOption,
		ChangeSecondsOption,
		BreakCountOption,
		EndOption
	];

	public static string Usage =>
		"""
		Usage: SemiPlot.Tools.ArchiveSeeder --connection <string> --end <local timestamp> [options]

		  --connection        Npgsql connection string for the scada_writer role. Required.
		  --end               Exclusive end of the generated span, without a time zone,
		                      for example 2026-01-02T00:00:00. Required, so that two runs of
		                      the same seed produce the same archive.
		  --admin-connection  Npgsql connection string used to fill semiplot_tags. Optional.
		  --days              Whole days covered, ending at --end. Default 1.
		  --pens              Pens taken round-robin from the catalogue. Default 8.
		  --seed              Generator seed. Default 1.
		  --change-seconds    Mean interval between value changes. Default 5.
		  --break-count       Breaks placed across the span. Default 4. A break needs up to 10 minutes
		                      of downtime with 5 minutes of archiving on either side, so a day holds
		                      at most 72 of them.
		  --follow            Runs the demo writer instead of seeding: it appends rows on a wall-clock
		                      cadence and takes options of its own. Pass --follow for those.
		""";

	public DateTime Start => End - TimeSpan.FromDays(Days);

	public static Result<SeederOptions> Parse(IReadOnlyList<string> arguments)
	{
		var tokens = OptionTokens.Read(arguments, _knownOptions);

		return tokens.IsFailed ? Result.Fail<SeederOptions>(tokens.Errors) : Build(tokens.Value);
	}

	private static Result<SeederOptions> Build(IReadOnlyDictionary<string, string> values)
	{
		if (!values.TryGetValue(ConnectionOption, out var connection) || connection.Length == 0)
		{
			return Result.Fail<SeederOptions>("Option '--connection' is required.");
		}

		if (!values.TryGetValue(EndOption, out var endText))
		{
			return Result.Fail<SeederOptions>(
				"Option '--end' is required: a floating 'now' would make two runs of the same seed differ.");
		}

		var end = ReadEnd(endText);
		var days = OptionTokens.ReadNumber(
			values, DaysOption, DefaultDays, NumberStyles.Integer, OptionTokens.WholeNumber);
		var pens = OptionTokens.ReadNumber(
			values, PensOption, DefaultPenCount, NumberStyles.Integer, OptionTokens.WholeNumber);
		var seed = OptionTokens.ReadNumber(
			values, SeedOption, DefaultSeed, NumberStyles.Integer, OptionTokens.WholeNumber);
		var changeSeconds = OptionTokens.ReadNumber(
			values, ChangeSecondsOption, DefaultChangeSeconds, NumberStyles.Float, OptionTokens.PlainNumber);
		var breakCount = OptionTokens.ReadNumber(
			values, BreakCountOption, DefaultBreakCount, NumberStyles.Integer, OptionTokens.WholeNumber);

		var merged = Result.Merge(end, days, pens, seed, changeSeconds, breakCount);

		if (merged.IsFailed)
		{
			return Result.Fail<SeederOptions>(merged.Errors);
		}

		values.TryGetValue(AdminConnectionOption, out var adminConnection);

		return Validate(
			new SeederOptions(
				connection,
				end.Value,
				days.Value,
				pens.Value,
				seed.Value,
				changeSeconds.Value,
				breakCount.Value,
				adminConnection));
	}

	// Ordered rather than merged: every check after the span reads Start, and a span that reaches past
	// the earliest representable timestamp throws out of the subtraction behind it.
	private static Result<SeederOptions> Validate(SeederOptions options)
	{
		Func<SeederOptions, Result>[] checks =
			[ValidateSpan, ValidatePenCount, ValidateChangeRate, ValidateBreaks];

		foreach (var check in checks)
		{
			var outcome = check(options);

			if (outcome.IsFailed)
			{
				return Result.Fail<SeederOptions>(outcome.Errors);
			}
		}

		return Result.Ok(options);
	}

	// A partition's upper bound is midnight of the day after the day it covers, which the last
	// representable day has not got.
	private static Result ValidateSpan(SeederOptions options)
	{
		var latestEnd = DateTime.MaxValue.Date;

		if (options.End > latestEnd)
		{
			return Result.Fail($"Option '--end' must not exceed {latestEnd:O}, got {options.End:O}.");
		}

		if (options.Days < 1)
		{
			return Result.Fail($"Option '--days' must be at least 1, got {options.Days}.");
		}

		// Integer division on the ticks rather than TotalDays: TotalDays is a rounded double, and a
		// quotient a hair under a whole day rounds up to it, raising the bound by a day and putting Start
		// back past DateTime.MinValue.
		var latestDays = (int)((options.End - DateTime.MinValue).Ticks / TimeSpan.TicksPerDay);

		if (options.Days > latestDays)
		{
			return Result.Fail(
				$"Option '--days' must not exceed {latestDays} for an end of {options.End:O}, got {options.Days}.");
		}

		return Result.Ok();
	}

	private static Result ValidatePenCount(SeederOptions options)
	{
		if (options.PenCount < 1)
		{
			return Result.Fail($"Option '--pens' must be at least 1, got {options.PenCount}.");
		}

		var catalogSize = SyntheticPenCatalog.Build().Count;

		if (options.PenCount > catalogSize)
		{
			return Result.Fail(
				$"Option '--pens' must not exceed the catalogue of {catalogSize} pens, got {options.PenCount}.");
		}

		return Result.Ok();
	}

	// NaN fails every comparison, so a bare `<= 0` check lets it through, and Infinity survives it too
	// and overflows the interval arithmetic. The span is the ceiling above: a mean interval longer than
	// the whole run produces no change at all.
	private static Result ValidateChangeRate(SeederOptions options)
	{
		if (!double.IsFinite(options.ChangeSeconds) || options.ChangeSeconds <= 0.0)
		{
			return Result.Fail(
				$"Option '--change-seconds' must be a finite number greater than 0, got {options.ChangeSeconds}.");
		}

		var span = options.End - options.Start;

		if (options.ChangeSeconds > span.TotalSeconds)
		{
			return Result.Fail(
				$"Option '--change-seconds' must not exceed the {span.TotalSeconds:0} s span, "
					+ $"got {options.ChangeSeconds}.");
		}

		return Result.Ok();
	}

	// The same rule BreakPlan.Create enforces, applied where the value came from, so a count the span
	// cannot hold is a rejected option with usage rather than a stack trace out of the generator.
	private static Result ValidateBreaks(SeederOptions options)
	{
		if (options.BreakCount < 0)
		{
			return Result.Fail($"Option '--break-count' must not be negative, got {options.BreakCount}.");
		}

		var maximumBreaks = BreakPlan.MaximumBreaks(options.End - options.Start);

		if (options.BreakCount > maximumBreaks)
		{
			return Result.Fail(
				$"Option '--break-count' must not exceed {maximumBreaks} across the {options.Days}-day span, "
					+ $"got {options.BreakCount}.");
		}

		return Result.Ok();
	}

	// The archive column is 'timestamp(3) without time zone', so an offset-bearing bound would be
	// silently reinterpreted rather than rejected.
	private static Result<DateTime> ReadEnd(string text)
	{
		if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			return Result.Fail<DateTime>(
				$"Option '--end' expects a timestamp such as 2026-01-02T00:00:00, got '{text}'.");
		}

		if (parsed.Kind != DateTimeKind.Unspecified)
		{
			return Result.Fail<DateTime>($"Option '--end' must carry no time zone, got '{text}'.");
		}

		return Result.Ok(parsed);
	}
}
