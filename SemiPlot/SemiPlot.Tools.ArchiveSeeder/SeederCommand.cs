using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;

namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// The command line: one root command, `--follow` selects the demo writer, everything else is a seeding
/// run. Every single-option rule lives in that option's parser; the rules that need two options, and the
/// mode itself, are decided in <see cref="Interpret"/> once every option has parsed.
/// </summary>
public static class SeederCommand
{
	private static readonly Option<string> _connection = new("--connection")
	{
		Description = "Npgsql connection string for the scada_writer role.",
		Required = true
	};

	private static readonly Option<string?> _adminConnection = new("--admin-connection")
	{
		Description = "Npgsql connection string used to fill semiplot_tags. Seeding only."
	};

	private static readonly Option<DateTime?> _end = new("--end")
	{
		Description = "Exclusive end of the generated span, without a time zone, for example "
					  + "2026-01-02T00:00:00. Seeding only, and required there so that two runs of the same seed "
					  + "produce the same archive.",
		CustomParser = ParseEnd
	};

	private static readonly Option<int> _days = new("--days")
	{
		Description = "Whole days covered, ending at --end. Seeding only.",
		DefaultValueFactory = _ => SeederOptions.DefaultDays,
		CustomParser = result => ParseWholeNumber(result, "--days", 1, int.MaxValue)
	};

	private static readonly Option<int> _pens = new("--pens")
	{
		Description = "Pens taken round-robin from the catalogue.",
		DefaultValueFactory = _ => SeederOptions.DefaultPenCount,
		CustomParser = result => ParseWholeNumber(result, "--pens", 1, SyntheticPenCatalog.Build().Count)
	};

	private static readonly Option<long> _seed = new("--seed")
	{
		Description = "Generator seed.",
		DefaultValueFactory = _ => SeederOptions.DefaultSeed,
		CustomParser = ParseSeed
	};

	private static readonly Option<double> _changeSeconds = new("--change-seconds")
	{
		Description = "Interval between value changes, in seconds.",
		DefaultValueFactory = _ => SeederOptions.DefaultChangeSeconds,
		CustomParser = result => ParseSeconds(result, "--change-seconds") ?? SeederOptions.DefaultChangeSeconds
	};

	private static readonly Option<int> _breakCount = new("--break-count")
	{
		Description = "Breaks placed across the span. Seeding only. A break needs up to 10 minutes of "
					  + "downtime with 5 minutes of archiving on either side, so a day holds at most 72 of them.",
		DefaultValueFactory = _ => SeederOptions.DefaultBreakCount,
		CustomParser = result => ParseWholeNumber(result, "--break-count", 0, int.MaxValue)
	};

	private static readonly Option<double?> _follow = new("--follow")
	{
		Description = "Seconds between ticks, and the switch that selects the demo writer: each tick appends "
					  + "the raw rows of the wall-clock span since the previous one and thins them into the coarse "
					  + "layers. It seeds nothing.",
		CustomParser = result => ParseSeconds(result, "--follow")
	};

	private static readonly (Option Option, string Name)[] _seedingOnly =
	[
		(_end, "--end"),
		(_days, "--days"),
		(_breakCount, "--break-count"),
		(_adminConnection, "--admin-connection")
	];

	private static readonly RootCommand _root = Build();

	/// <summary>
	/// Parses without running: the options a run would use, or the errors that stop it.
	/// </summary>
	public static SeederRun Parse(IReadOnlyList<string> arguments)
	{
		return Interpret(_root.Parse([.. arguments]));
	}

	/// <summary>
	/// Parses and dispatches. Errors print with a pointer at <c>--help</c> and exit 1; <c>--help</c> and
	/// <c>--version</c> take their built-in path.
	/// </summary>
	public static async Task<int> RunAsync(
		string[] arguments,
		Func<SeederOptions, Task<int>> seed,
		Func<FollowOptions, Task<int>> follow)
	{
		var parsed = _root.Parse(arguments);

		if (parsed.Errors.Count == 0 && parsed.Action is not null)
		{
			return await parsed.InvokeAsync();
		}

		var run = Interpret(parsed);

		if (run.Errors.Count > 0)
		{
			foreach (var error in run.Errors)
			{
				Console.Error.WriteLine(error);
			}

			Console.Error.WriteLine();
			Console.Error.WriteLine("Run with --help for the option list.");

			return 1;
		}

		return run.Follow is { } followOptions ? await follow(followOptions) : await seed(run.Seed!);
	}

	private static RootCommand Build()
	{
		var root = new RootCommand("Fills a SemiBase-provisioned archive with a generated slice, or keeps one live.");

		foreach (var option in new Option[]
				 {
					 _connection, _end, _adminConnection, _days, _pens, _seed, _changeSeconds, _breakCount, _follow
				 })
		{
			root.Options.Add(option);
		}

		_connection.Validators.Add(result =>
		{
			if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
			{
				result.AddError("Option '--connection' must not be blank.");
			}
		});

		return root;
	}

	// Invariant culture rather than the machine's: a decimal comma or a thousands separator would otherwise
	// parse on one operator's machine and fail on the next. The range rule sits beside the parse so a value
	// is checked exactly once, where it came from.
	private static int ParseWholeNumber(ArgumentResult result, string name, int minimum, int maximum)
	{
		var text = result.Tokens[^1].Value;

		if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
		{
			result.AddError($"Option '{name}' expects a whole number, got '{text}'.");

			return minimum;
		}

		if (value < minimum)
		{
			result.AddError($"Option '{name}' must be at least {minimum}, got {value}.");
		}
		else if (value > maximum)
		{
			result.AddError(
				name == "--pens"
					? $"Option '{name}' must not exceed the catalogue of {maximum} pens, got {value}."
					: $"Option '{name}' must be at most {maximum}, got {value}.");
		}

		return value;
	}

	private static long ParseSeed(ArgumentResult result)
	{
		var text = result.Tokens[^1].Value;

		if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
		{
			return value;
		}

		result.AddError($"Option '--seed' expects a whole number, got '{text}'.");

		return SeederOptions.DefaultSeed;
	}

	// NaN fails every comparison, so a bare `<= 0` check lets it through, and Infinity survives it too and
	// overflows the interval arithmetic.
	private static double? ParseSeconds(ArgumentResult result, string name)
	{
		var text = result.Tokens[^1].Value;

		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
		{
			result.AddError($"Option '{name}' expects a number, got '{text}'.");

			return null;
		}

		if (!double.IsFinite(seconds) || seconds <= 0.0 || seconds > FollowOptions.MaximumSeconds)
		{
			result.AddError(
				$"Option '{name}' must be a finite number greater than 0 and at most "
				+ $"{FollowOptions.MaximumSeconds:0}, got {seconds}.");

			return null;
		}

		return seconds;
	}

	// The archive column is 'timestamp(3) without time zone', so an offset-bearing bound would be silently
	// reinterpreted rather than rejected. The --end ceiling is the partition walk, not the span:
	// PartitionScript.CoveredDays steps a day past the last day it covers.
	private static DateTime? ParseEnd(ArgumentResult result)
	{
		var text = result.Tokens[^1].Value;

		if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
		{
			result.AddError($"Option '--end' expects a timestamp such as 2026-01-02T00:00:00, got '{text}'.");

			return null;
		}

		if (parsed.Kind != DateTimeKind.Unspecified)
		{
			result.AddError($"Option '--end' must carry no time zone, got '{text}'.");

			return null;
		}

		if (parsed > DateTime.MaxValue.Date)
		{
			result.AddError(
				$"Option '--end' must not fall inside {DateTime.MaxValue.Date:yyyy-MM-dd}, the last representable "
				+ $"day, got {parsed:O}: its day partitions cannot be walked.");

			return null;
		}

		return parsed;
	}

	// The two modes disagree about which options exist and about the ceilings the span sets, and nothing
	// else. Both are decided here, after the parse, so no value is read off an option that failed to parse.
	private static SeederRun Interpret(ParseResult parsed)
	{
		var errors = parsed.Errors.Select(error => error.Message).ToList();

		if (errors.Count > 0)
		{
			return new SeederRun(null, null, errors);
		}

		var connection = parsed.GetValue(_connection)!;
		var pens = parsed.GetValue(_pens);
		var seed = parsed.GetValue(_seed);
		var changeSeconds = parsed.GetValue(_changeSeconds);

		if (parsed.GetValue(_follow) is { } seconds)
		{
			foreach (var (option, name) in _seedingOnly)
			{
				if (parsed.GetResult(option) is { Implicit: false })
				{
					errors.Add(
						$"Option '{name}' belongs to a seeding run: --follow appends to an archive somebody else "
						+ "seeded, so it states no span, plants no breaks and fills no tag catalogue.");
				}
			}

			return errors.Count > 0
				? new SeederRun(null, null, errors)
				: new SeederRun(
					null,
					new FollowOptions(connection, TimeSpan.FromSeconds(seconds), pens, seed, changeSeconds),
					errors);
		}

		if (parsed.GetValue(_end) is not { } end)
		{
			errors.Add("Option '--end' is required: a floating 'now' would make two runs of the same seed differ.");

			return new SeederRun(null, null, errors);
		}

		var days = parsed.GetValue(_days);
		var breakCount = parsed.GetValue(_breakCount);
		var span = TimeSpan.FromDays(days);
		var maximumBreaks = BreakPlan.MaximumBreaks(span);

		if (changeSeconds > span.TotalSeconds)
		{
			errors.Add(
				$"Option '--change-seconds' must not exceed the {span.TotalSeconds:0} s span, got {changeSeconds}.");
		}

		if (breakCount > maximumBreaks)
		{
			errors.Add(
				$"Option '--break-count' must not exceed {maximumBreaks} across the {days}-day span, got {breakCount}.");
		}

		return errors.Count > 0
			? new SeederRun(null, null, errors)
			: new SeederRun(
				new SeederOptions(
					connection,
					end,
					days,
					pens,
					seed,
					changeSeconds,
					breakCount,
					parsed.GetValue(_adminConnection)),
				null,
				errors);
	}
}
