using AwesomeAssertions;

using SemiPlot.UI;

using Serilog.Events;

using Xunit;

namespace SemiPlot.Tests.UI;

[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class StartupOptionsTests
{
	[Fact]
	public void Parse_EmptyArgs_ReturnsDefaults()
	{
		var options = StartupOptions.Parse([]);

		options.ConfigDir.Should().Be(@"C:\DISTR\Config\SemiPlot");
		options.LogFilePath.Should().Be(@"C:\DISTR\Logs\SemiPlot\semiplot.log");
		options.LoggingLevel.Should().Be(LogEventLevel.Warning);
	}

	[Fact]
	public void Parse_ConfigDir_TakesFollowingValue()
	{
		var options = StartupOptions.Parse(["--config-dir", @"D:\bench\config"]);

		options.ConfigDir.Should().Be(@"D:\bench\config");
		options.LogFilePath.Should().Be(StartupOptions.DefaultLogFilePath);
	}

	[Fact]
	public void Parse_LogFile_TakesFollowingValue()
	{
		var options = StartupOptions.Parse(["--log-file", @"D:\bench\semiplot.log"]);

		options.LogFilePath.Should().Be(@"D:\bench\semiplot.log");
		options.ConfigDir.Should().Be(StartupOptions.DefaultConfigDir);
	}

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
		var options = StartupOptions.Parse(["--logging-level", value]);

		options.LoggingLevel.Should().Be(expected);
	}

	[Fact]
	public void Parse_UnknownLoggingLevel_FallsBackToDefault()
	{
		var options = StartupOptions.Parse(["--logging-level", "chatty"]);

		options.LoggingLevel.Should().Be(StartupOptions.DefaultLoggingLevel);
	}

	[Fact]
	public void Parse_ValuedArgumentLastWithNoValue_KeepsDefault()
	{
		var options = StartupOptions.Parse(["--config-dir"]);

		options.ConfigDir.Should().Be(StartupOptions.DefaultConfigDir);
	}

	[Fact]
	public void Parse_UnknownArgument_IsIgnored()
	{
		var options = StartupOptions.Parse(["--nonsense", "value", "--valueless-nonsense"]);

		options.ConfigDir.Should().Be(StartupOptions.DefaultConfigDir);
		options.LogFilePath.Should().Be(StartupOptions.DefaultLogFilePath);
		options.LoggingLevel.Should().Be(StartupOptions.DefaultLoggingLevel);
	}

	[Fact]
	public void Parse_AllArguments_AreApplied()
	{
		var options = StartupOptions.Parse(
		[
			"--config-dir", @"D:\bench\config",
			"--log-file", @"D:\bench\semiplot.log",
			"--logging-level", "debug"
		]);

		options.Should().Be(
			new StartupOptions(
				@"D:\bench\config",
				@"D:\bench\semiplot.log",
				LogEventLevel.Debug));
	}
}
