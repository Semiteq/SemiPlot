using AwesomeAssertions;

using FluentResults;

using SemiPlot.UI;

using Serilog.Events;

using Xunit;

namespace SemiPlot.Tests.Unit.UI;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class StartupOptionsTests
{
	[Theory]
	[InlineData("verbose", LogEventLevel.Verbose)]
	[InlineData("debug", LogEventLevel.Debug)]
	[InlineData("info", LogEventLevel.Information)]
	// Serilog's own name for the level, which an operator reads off a Serilog configuration and types.
	[InlineData("information", LogEventLevel.Information)]
	[InlineData("warning", LogEventLevel.Warning)]
	[InlineData("error", LogEventLevel.Error)]
	[InlineData("fatal", LogEventLevel.Fatal)]
	[InlineData("DEBUG", LogEventLevel.Debug)]
	public void Parse_LoggingLevel_TakesFollowingValue(string value, LogEventLevel expected)
	{
		var result = StartupOptions.Parse(All(loggingLevel: value));

		result.IsSuccess.Should().BeTrue();
		result.Value.LoggingLevel.Should().Be(expected);
	}

	[Fact]
	public void Parse_AllArguments_AreApplied()
	{
		var result = StartupOptions.Parse(All());

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().Be(
			new StartupOptions(
				@"D:\bench\config",
				@"D:\bench\semiplot.log",
				LogEventLevel.Debug));
	}

	[Fact]
	public void Parse_ArgumentsInAnyOrder_AreApplied()
	{
		var result = StartupOptions.Parse(
		[
			"--logging-level", "debug",
			"--log-file", @"D:\bench\semiplot.log",
			"--config-dir", @"D:\bench\config"
		]);

		result.IsSuccess.Should().BeTrue();
		result.Value.ConfigDir.Should().Be(@"D:\bench\config");
	}

	[Fact]
	public void Parse_EmptyArgs_FailsNamingTheFirstRequiredKey()
	{
		var error = Failure(StartupOptions.Parse([]));

		error.Kind.Should().Be(StartupArgumentsProblem.Missing);
		error.Key.Should().Be(StartupOptions.ConfigDirKey);
	}

	[Theory]
	[InlineData(StartupOptions.ConfigDirKey)]
	[InlineData(StartupOptions.LogFileKey)]
	[InlineData(StartupOptions.LoggingLevelKey)]
	public void Parse_WithoutOneKey_FailsNamingThatKey(string missing)
	{
		var error = Failure(StartupOptions.Parse(SkipValueOf(All(), missing)));

		error.Kind.Should().Be(StartupArgumentsProblem.Missing);
		error.Key.Should().Be(missing);
	}

	[Fact]
	public void Parse_ValuedArgumentLastWithNoValue_FailsNamingTheKey()
	{
		var error = Failure(StartupOptions.Parse(["--config-dir"]));

		error.Kind.Should().Be(StartupArgumentsProblem.ValueMissing);
		error.Key.Should().Be(StartupOptions.ConfigDirKey);
	}

	// An empty value passes the "a value follows" check and then reaches Serilog or the section reader as
	// a path nobody typed, so it is refused where every other unusable argument is.
	[Theory]
	[InlineData(StartupOptions.ConfigDirKey, "")]
	[InlineData(StartupOptions.ConfigDirKey, "   ")]
	[InlineData(StartupOptions.LogFileKey, "")]
	[InlineData(StartupOptions.LoggingLevelKey, "")]
	public void Parse_BlankValue_FailsNamingThatKey(string key, string value)
	{
		var error = Failure(StartupOptions.Parse(WithValue(All(), key, value)));

		error.Kind.Should().Be(StartupArgumentsProblem.ValueMissing);
		error.Key.Should().Be(key);
	}

	[Fact]
	public void Parse_RepeatedKey_TakesTheLastValue()
	{
		var result = StartupOptions.Parse(
			[.. All(), StartupOptions.ConfigDirKey, @"D:\second\config"]);

		result.IsSuccess.Should().BeTrue();
		result.Value.ConfigDir.Should().Be(@"D:\second\config");
	}

	[Fact]
	public void LoggingLevelValues_AreExactlyTheLevelsParseAccepts()
	{
		var accepted = StartupOptions.LoggingLevelValues.Split(", ");

		var levels = accepted
			.Select(value => StartupOptions.Parse(All(loggingLevel: value)))
			.ToList();

		levels.Should().OnlyContain(result => result.IsSuccess);
		levels.Select(result => result.Value.LoggingLevel).Distinct()
			.Should().BeEquivalentTo(Enum.GetValues<LogEventLevel>());
	}

	[Fact]
	public void Parse_UnknownArgument_FailsNamingWhatWasGiven()
	{
		var error = Failure(StartupOptions.Parse(["--nonsense", "value"]));

		error.Kind.Should().Be(StartupArgumentsProblem.Unknown);
		error.Key.Should().Be("--nonsense");
	}

	[Fact]
	public void Parse_UnknownArgumentAfterAValidOne_StillFails()
	{
		var error = Failure(StartupOptions.Parse([.. All(), "--valueless-nonsense"]));

		error.Kind.Should().Be(StartupArgumentsProblem.Unknown);
		error.Key.Should().Be("--valueless-nonsense");
	}

	[Fact]
	public void Parse_UnknownLoggingLevel_FailsNamingTheAcceptedSet()
	{
		var error = Failure(StartupOptions.Parse(All(loggingLevel: "chatty")));

		error.Kind.Should().Be(StartupArgumentsProblem.ValueInvalid);
		error.Key.Should().Be(StartupOptions.LoggingLevelKey);
		error.AcceptedValues.Should().Be(StartupOptions.LoggingLevelValues);
	}

	[Fact]
	public void Parse_UnknownLoggingLevel_KeepsTheValueOutOfTheMessage()
	{
		Failure(StartupOptions.Parse(All(loggingLevel: "chatty"))).Message.Should().NotContain("chatty");
	}

	private static string[] All(
		string configDir = @"D:\bench\config",
		string logFile = @"D:\bench\semiplot.log",
		string loggingLevel = "debug")
	{
		return
		[
			StartupOptions.ConfigDirKey, configDir,
			StartupOptions.LogFileKey, logFile,
			StartupOptions.LoggingLevelKey, loggingLevel
		];
	}

	private static string[] WithValue(string[] args, string key, string value)
	{
		var index = Array.IndexOf(args, key);
		var replaced = args.ToArray();
		replaced[index + 1] = value;

		return replaced;
	}

	private static string[] SkipValueOf(string[] args, string key)
	{
		var index = Array.IndexOf(args, key);

		return [.. args[..index], .. args[(index + 2)..]];
	}

	private static StartupArgumentsError Failure(Result<StartupOptions> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.Should().ContainSingle().Which.Should().BeOfType<StartupArgumentsError>().Which;
	}
}
