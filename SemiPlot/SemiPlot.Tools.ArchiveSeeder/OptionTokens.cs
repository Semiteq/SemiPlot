using System.Globalization;
using System.Numerics;

using FluentResults;

namespace SemiPlot.Tools.ArchiveSeeder;

// The '--name value' tokeniser both option types share. A seeding run and a follow run disagree about
// which options exist and about nothing else, so the unexpected-argument, unknown-option,
// missing-value and repeated-option rules live here once instead of once per option type.
public static class OptionTokens
{
	public const string WholeNumber = "a whole number";
	public const string PlainNumber = "a number";

	public static Result<IReadOnlyDictionary<string, string>> Read(
		IReadOnlyList<string> arguments,
		IReadOnlyList<string> knownOptions)
	{
		var values = new Dictionary<string, string>(StringComparer.Ordinal);

		for (var index = 0; index < arguments.Count; index++)
		{
			var argument = arguments[index];

			if (!argument.StartsWith("--", StringComparison.Ordinal))
			{
				return Fail($"Unexpected argument '{argument}'.");
			}

			var name = argument[2..];

			if (!knownOptions.Contains(name, StringComparer.Ordinal))
			{
				return Fail($"Unknown option '{argument}'.");
			}

			if (index + 1 >= arguments.Count)
			{
				return Fail($"Option '{argument}' requires a value.");
			}

			// A value that is itself an option means the previous one was left without a value; taking
			// it would report the failure against a later, innocent token.
			if (arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
			{
				return Fail($"Option '{argument}' requires a value, got the option '{arguments[index + 1]}'.");
			}

			if (!values.TryAdd(name, arguments[index + 1]))
			{
				return Fail($"Option '{argument}' is specified more than once.");
			}

			index++;
		}

		return Result.Ok<IReadOnlyDictionary<string, string>>(values);
	}

	public static Result<TNumber> ReadNumber<TNumber>(
		IReadOnlyDictionary<string, string> values,
		string name,
		TNumber fallback,
		NumberStyles styles,
		string expectation)
		where TNumber : INumberBase<TNumber>
	{
		if (!values.TryGetValue(name, out var text))
		{
			return Result.Ok(fallback);
		}

		return TNumber.TryParse(text, styles, CultureInfo.InvariantCulture, out var parsed)
			? Result.Ok(parsed)
			: Result.Fail<TNumber>($"Option '--{name}' expects {expectation}, got '{text}'.");
	}

	private static Result<IReadOnlyDictionary<string, string>> Fail(string message)
	{
		return Result.Fail<IReadOnlyDictionary<string, string>>(message);
	}
}
