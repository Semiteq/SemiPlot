using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;

namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// The command line: one root command, `--follow` selects the demo writer, everything else is a seeding
/// run, and `converge` is a subcommand. Every single-option rule lives in that option's parser; the mode
/// is decided in <see cref="Interpret"/> or <see cref="InterpretConverge"/> once every option has parsed.
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
					  + "2026-01-02T00:00:00. Required for seeding, so that two runs of the same seed produce "
					  + "the same archive; defaults to this machine's clock for converge.",
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
		(_adminConnection, "--admin-connection")
	];

	private static readonly Option<string> _convergeAdminConnection = new("--admin-connection")
	{
		Description = "Npgsql connection string on the maintenance database, for the superuser role.",
		Required = true
	};

	private static readonly Option<string> _configDir = new("--config-dir")
	{
		Description = "Directory archive-connection.yaml is written into.",
		Required = true
	};

	private static readonly Command _convergeCommand = BuildConverge();

	private static readonly RootCommand _root = Build();

	// A RootCommand carrying a Subcommand refuses every parse that names none unless the root has an
	// Action of its own (measured against System.CommandLine 2.0.2); this one is never invoked, only
	// compared by reference, so a real parse of root-only options still reaches Interpret manually.
	private static readonly CommandLineAction _rootAction = _root.Action!;

	/// <summary>
	/// Parses and dispatches. Errors print with a pointer at <c>--help</c> and exit 1; <c>--help</c> and
	/// <c>--version</c> take their built-in path.
	/// </summary>
	public static async Task<int> RunAsync(
		string[] arguments,
		Func<SeederOptions, Task<int>> seed,
		Func<FollowOptions, Task<int>> follow,
		Func<ConvergeOptions, Task<int>> converge)
	{
		var parsed = _root.Parse(arguments);

		if (parsed.Errors.Count == 0 && parsed.Action is not null && !ReferenceEquals(parsed.Action, _rootAction))
		{
			return await parsed.InvokeAsync();
		}

		return ReferenceEquals(parsed.CommandResult.Command, _convergeCommand)
			? await InterpretConverge(parsed, converge)
			: await Interpret(parsed, seed, follow);
	}

	private static RootCommand Build()
	{
		var root = new RootCommand("Fills a SemiBase-provisioned archive with a generated slice, keeps one "
									+ "live, or recreates the bench stand.");

		foreach (var option in new Option[]
				 {
					 _connection, _end, _adminConnection, _days, _pens, _seed, _changeSeconds, _follow
				 })
		{
			root.Options.Add(option);
		}

		_connection.Validators.Add(RejectBlank("--connection"));

		root.Subcommands.Add(_convergeCommand);

		// A no-op action, never invoked through InvokeAsync: its only job is to keep System.CommandLine
		// from refusing a converge-less parse once the subcommand above is registered.
		root.SetAction(_ => 0);

		return root;
	}

	// Shares --connection and --end with the root command: converge accepts the same connection string
	// shape and the same optional end bound, and System.CommandLine allows one Option under several commands.
	private static Command BuildConverge()
	{
		var converge = new Command(
			"converge",
			"Recreates the stand database from semiplot_provisioned, seeds it, fills the tag catalogue and "
			+ "writes the connection file. Bench only.");

		foreach (var option in new Option[] { _connection, _convergeAdminConnection, _configDir, _end, _changeSeconds })
		{
			converge.Options.Add(option);
		}

		_convergeAdminConnection.Validators.Add(RejectBlank("--admin-connection"));
		_configDir.Validators.Add(RejectBlank("--config-dir"));

		return converge;
	}

	private static Action<OptionResult> RejectBlank(string name)
	{
		return result =>
		{
			if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
			{
				result.AddError($"Option '{name}' must not be blank.");
			}
		};
	}

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

	private static Task<int> InterpretConverge(ParseResult parsed, Func<ConvergeOptions, Task<int>> converge)
	{
		var errors = parsed.Errors.Select(error => error.Message).ToList();

		if (errors.Count > 0)
		{
			return Report(errors);
		}

		return converge(new ConvergeOptions(
			parsed.GetValue(_connection)!,
			parsed.GetValue(_convergeAdminConnection)!,
			parsed.GetValue(_configDir)!,
			parsed.GetValue(_end),
			parsed.GetValue(_changeSeconds)));
	}

	// Both modes are decided here, after the parse, so no value is read off an option that failed to parse.
	private static Task<int> Interpret(
		ParseResult parsed,
		Func<SeederOptions, Task<int>> seed,
		Func<FollowOptions, Task<int>> follow)
	{
		var errors = parsed.Errors.Select(error => error.Message).ToList();

		if (errors.Count > 0)
		{
			return Report(errors);
		}

		var connection = parsed.GetValue(_connection)!;
		var pens = parsed.GetValue(_pens);
		var seedValue = parsed.GetValue(_seed);
		var changeSeconds = parsed.GetValue(_changeSeconds);

		if (parsed.GetValue(_follow) is { } seconds)
		{
			foreach (var (option, name) in _seedingOnly)
			{
				if (parsed.GetResult(option) is { Implicit: false })
				{
					errors.Add(
						$"Option '{name}' belongs to a seeding run: --follow appends to an archive somebody else "
						+ "seeded, so it states no span and fills no tag catalogue.");
				}
			}

			return errors.Count > 0
				? Report(errors)
				: follow(new FollowOptions(connection, TimeSpan.FromSeconds(seconds), pens, seedValue, changeSeconds));
		}

		if (parsed.GetValue(_end) is not { } end)
		{
			errors.Add("Option '--end' is required: a floating 'now' would make two runs of the same seed differ.");

			return Report(errors);
		}

		var days = parsed.GetValue(_days);
		var span = TimeSpan.FromDays(days);

		if (changeSeconds > span.TotalSeconds)
		{
			errors.Add(
				$"Option '--change-seconds' must not exceed the {span.TotalSeconds:0} s span, got {changeSeconds}.");
		}

		return errors.Count > 0
			? Report(errors)
			: seed(new SeederOptions(
				connection,
				end,
				days,
				pens,
				seedValue,
				changeSeconds,
				SeederOptions.DefaultBreakCount,
				parsed.GetValue(_adminConnection)));
	}

	private static Task<int> Report(IReadOnlyList<string> errors)
	{
		foreach (var error in errors)
		{
			Console.Error.WriteLine(error);
		}

		Console.Error.WriteLine();
		Console.Error.WriteLine("Run with --help for the option list.");

		return Task.FromResult(1);
	}
}
