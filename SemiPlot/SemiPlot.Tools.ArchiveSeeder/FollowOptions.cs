using System.Globalization;

using FluentResults;

namespace SemiPlot.Tools.ArchiveSeeder;

// The demo writer's options, kept apart from SeederOptions rather than folded into it. A seeding run
// requires --end so that two runs of the same seed produce the same archive, and its ordered validation
// chain reads Start behind every later check; a follow run has no end at all. Separating the two leaves
// --end unconditionally required where it belongs and leaves the bench template's digest, which is
// taken over SeederOptions' own members, where it was.
public sealed record FollowOptions(
	string ConnectionString,
	TimeSpan Interval,
	int PenCount,
	long Seed,
	double ChangeSeconds)
{
	// A follow run states no span of its own, so the ceiling a seeding run takes from its span is a
	// literal here. A change interval longer than a day emits nothing anyway, and a value far
	// above it overflows the tick arithmetic behind the generator.
	public const double MaximumSeconds = 86400.0;

	private const string ConnectionOption = "connection";
	private const string FollowOption = "follow";
	private const string PensOption = "pens";
	private const string SeedOption = "seed";
	private const string ChangeSecondsOption = "change-seconds";

	private const string EndOption = "end";
	private const string DaysOption = "days";
	private const string BreakCountOption = "break-count";
	private const string AdminConnectionOption = "admin-connection";

	// The seeding options are known to the tokeniser here so that one typed into a follow run is
	// answered with what a follow run does, rather than with 'Unknown option'.
	private static readonly string[] _seedingOptions =
	[
		EndOption,
		DaysOption,
		BreakCountOption,
		AdminConnectionOption
	];

	private static readonly string[] _knownOptions =
	[
		ConnectionOption,
		FollowOption,
		PensOption,
		SeedOption,
		ChangeSecondsOption,
		EndOption,
		DaysOption,
		BreakCountOption,
		AdminConnectionOption
	];

	public static string Usage =>
		"""
		Usage: SemiPlot.Tools.ArchiveSeeder --connection <string> --follow <seconds> [options]

		  --connection      Npgsql connection string for the scada_writer role. Required.
		  --follow          Seconds between ticks, and the switch that selects this mode. Each tick
		                    appends the raw rows of the wall-clock span since the previous one, so the
		                    archive's live edge keeps moving. Required.
		  --pens            Pens taken round-robin from the catalogue. Default 8.
		  --seed            Generator seed. Default 1.
		  --change-seconds  Interval between value changes. Default 5.

		A follow run appends raw rows and thins them into the coarse layers, and seeds nothing, so
		--end, --days, --break-count and --admin-connection are rejected here.
		""";

	// The raw argument list decides the mode, ahead of either parser: a seeding run must reach
	// SeederOptions.Parse on exactly the path it reaches it on today.
	public static bool IsRequested(IReadOnlyList<string> arguments)
	{
		return arguments.Contains("--" + FollowOption, StringComparer.Ordinal);
	}

	public static Result<FollowOptions> Parse(IReadOnlyList<string> arguments)
	{
		var tokens = OptionTokens.Read(arguments, _knownOptions);

		return tokens.IsFailed ? Result.Fail<FollowOptions>(tokens.Errors) : Build(tokens.Value);
	}

	private static Result<FollowOptions> Build(IReadOnlyDictionary<string, string> values)
	{
		if (!values.TryGetValue(ConnectionOption, out var connection) || connection.Length == 0)
		{
			return Result.Fail<FollowOptions>("Option '--connection' is required.");
		}

		var seeding = RejectSeedingOptions(values);

		if (seeding.IsFailed)
		{
			return Result.Fail<FollowOptions>(seeding.Errors);
		}

		if (!values.ContainsKey(FollowOption))
		{
			return Result.Fail<FollowOptions>("Option '--follow' is required: it carries the tick cadence.");
		}

		var interval = OptionTokens.ReadNumber(
			values, FollowOption, 0.0, NumberStyles.Float, OptionTokens.PlainNumber);
		var pens = OptionTokens.ReadNumber(
			values, PensOption, SeederOptions.DefaultPenCount, NumberStyles.Integer, OptionTokens.WholeNumber);
		var seed = OptionTokens.ReadNumber(
			values, SeedOption, SeederOptions.DefaultSeed, NumberStyles.Integer, OptionTokens.WholeNumber);
		var changeSeconds = OptionTokens.ReadNumber(
			values, ChangeSecondsOption, SeederOptions.DefaultChangeSeconds, NumberStyles.Float,
			OptionTokens.PlainNumber);

		var merged = Result.Merge(interval, pens, seed, changeSeconds);

		if (merged.IsFailed)
		{
			return Result.Fail<FollowOptions>(merged.Errors);
		}

		// Ahead of the record rather than inside the chain below: TimeSpan.FromSeconds throws on a
		// value the chain has not seen yet.
		var cadence = ValidateSeconds(FollowOption, interval.Value);

		if (cadence.IsFailed)
		{
			return Result.Fail<FollowOptions>(cadence.Errors);
		}

		return Validate(
			new FollowOptions(
				connection,
				TimeSpan.FromSeconds(interval.Value),
				pens.Value,
				seed.Value,
				changeSeconds.Value));
	}

	private static Result<FollowOptions> Validate(FollowOptions options)
	{
		Func<FollowOptions, Result>[] checks = [ValidatePenCount, ValidateChangeRate];

		foreach (var check in checks)
		{
			var outcome = check(options);

			if (outcome.IsFailed)
			{
				return Result.Fail<FollowOptions>(outcome.Errors);
			}
		}

		return Result.Ok(options);
	}

	private static Result RejectSeedingOptions(IReadOnlyDictionary<string, string> values)
	{
		foreach (var name in _seedingOptions)
		{
			if (values.ContainsKey(name))
			{
				return Result.Fail(
					$"Option '--{name}' belongs to a seeding run: --follow appends to an archive somebody "
						+ "else seeded, so it states no span, plants no breaks and fills no tag catalogue.");
			}
		}

		return Result.Ok();
	}

	private static Result ValidatePenCount(FollowOptions options)
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

	private static Result ValidateChangeRate(FollowOptions options)
	{
		return ValidateSeconds(ChangeSecondsOption, options.ChangeSeconds);
	}

	// NaN fails every comparison, so a bare `<= 0` check lets it through, and Infinity survives it too
	// and overflows the arithmetic behind the tick.
	private static Result ValidateSeconds(string name, double seconds)
	{
		if (!double.IsFinite(seconds) || seconds <= 0.0 || seconds > MaximumSeconds)
		{
			return Result.Fail(
				$"Option '--{name}' must be a finite number greater than 0 and at most "
					+ $"{MaximumSeconds:0}, got {seconds}.");
		}

		return Result.Ok();
	}
}
