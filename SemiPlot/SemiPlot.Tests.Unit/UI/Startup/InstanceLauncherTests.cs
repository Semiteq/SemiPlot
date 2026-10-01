using System.Diagnostics;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Serilog.Events;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>No test starts a process.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class InstanceLauncherTests
{
	private const string HostPath = TestLaunch.HostPath;

	private const string EntryAssemblyPath = "/opt/semiplot/SemiPlot.UI.dll";

	private static readonly string[] _launchKeys =
		["--config-dir", "/etc/semiplot", "--log-file", "/var/log/semiplot.log", "--logging-level", "Warning"];

	private static readonly StartupOptions _options =
		new("/etc/semiplot", "/var/log/semiplot.log", LogEventLevel.Warning);

	[Fact]
	public void TheStartInfoCarriesTheLaunchKeys()
	{
		var startInfo = InstanceLauncher.BuildStartInfo(HostPath, entryAssemblyPath: null, _options);

		startInfo.FileName.Should().Be(HostPath);
		startInfo.UseShellExecute.Should().BeFalse();
		startInfo.ArgumentList.Should().Equal(_launchKeys);
		StartupOptions.Parse([.. startInfo.ArgumentList]).Value.Should().Be(_options);
	}

	[Fact]
	public void AnApphost_WithTheEntryAssemblyKnown_GetsTheLaunchKeysAlone()
	{
		var startInfo = InstanceLauncher.BuildStartInfo(HostPath, EntryAssemblyPath, _options);

		startInfo.ArgumentList.Should().Equal(_launchKeys);
	}

	[Theory]
	[InlineData("/usr/share/dotnet/dotnet")]
	[InlineData("/usr/share/dotnet/dotnet.exe")]
	public void TheDotnetHost_GetsTheEntryAssemblyFirst(string muxerPath)
	{
		var startInfo = InstanceLauncher.BuildStartInfo(muxerPath, EntryAssemblyPath, _options);

		startInfo.ArgumentList.Should().Equal([EntryAssemblyPath, .. _launchKeys]);
	}

	[Fact]
	public void APathWithSpaces_StaysOneArgument()
	{
		var spaced = new StartupOptions("/my config/app", "/my logs/semi plot.log", LogEventLevel.Information);

		var startInfo = InstanceLauncher.BuildStartInfo("/opt/Semi Plot/SemiPlot.UI", entryAssemblyPath: null, spaced);

		startInfo.ArgumentList.Should().Contain("/my config/app").And.Contain("/my logs/semi plot.log");
		startInfo.ArgumentList.Should().HaveCount(6);
	}

	[Fact]
	public void Start_HandsTheStartInfoToTheSeam()
	{
		var started = new List<ProcessStartInfo>();
		var launcher = new InstanceLauncher(_options, HostPath, EntryAssemblyPath, started.Add);

		launcher.Start().IsSuccess.Should().BeTrue();

		started.Should().ContainSingle().Which.FileName.Should().Be(HostPath);
	}

	[Fact]
	public void Start_FailsWhenTheProcessDoesNotStart()
	{
		var thrown = new InvalidOperationException("no such file");
		var launcher = new InstanceLauncher(_options, HostPath, EntryAssemblyPath, _ => throw thrown);

		var result = launcher.Start();

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ExceptionalError>()
			.Which.Exception.Should().BeSameAs(thrown);
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("", EntryAssemblyPath)]
	[InlineData("/usr/share/dotnet/dotnet", null)]
	[InlineData("/usr/share/dotnet/dotnet.exe", "")]
	public void Start_FailsWhenNoHostCanBeNamed(string? processPath, string? entryAssemblyPath)
	{
		var started = new List<ProcessStartInfo>();
		var launcher = new InstanceLauncher(_options, processPath, entryAssemblyPath, started.Add);

		var result = launcher.Start();

		result.Errors.Should().ContainSingle().Which.Should().BeOfType<InstanceHostUnknownError>();
		started.Should().BeEmpty();
	}
}
