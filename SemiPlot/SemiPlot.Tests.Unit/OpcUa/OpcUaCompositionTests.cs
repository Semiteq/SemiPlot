using System.Diagnostics;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.OpcUa;
using SemiPlot.DataSource.OpcUa.Configuration;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

// Port 1 is closed, and no test here subscribes: resolving and disposing the source opens no socket.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class OpcUaCompositionTests
{
	private static readonly OpcUaConnectionSettings _settings =
		new("127.0.0.1", 1, Path.Combine(Path.GetTempPath(), "semiplot-unused-pki"));

	[Fact]
	public void TheLiveSourceIsTheOpcUaSource_RegisteredOnceAsASingleton()
	{
		using var provider = Build(new ServiceCollection().AddLogging());

		provider.GetServices<ILiveSource>().Should().ContainSingle().Which.Should().BeOfType<OpcUaLiveSource>();
		provider.GetRequiredService<ILiveSource>().Should().BeSameAs(provider.GetRequiredService<ILiveSource>());
	}

	// Disposed asynchronously: the synchronous Dispose hops to the pool, which a parallel suite can starve for seconds.
	[Fact]
	public async Task ResolvingAndDisposingWithNoSubscription_ReturnsAtOnce_AndTheSourceNeverLeftConnecting()
	{
		var provider = Build(new ServiceCollection().AddLogging());
		var source = provider.GetRequiredService<ILiveSource>();
		var states = new List<LiveSourceState>();
		var isCompleted = false;
		using var recording = source.States.Subscribe(states.Add, () => isCompleted = true);

		var watch = Stopwatch.StartNew();
		await provider.DisposeAsync();

		watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
		states.Should().Equal(LiveSourceState.Connecting);
		isCompleted.Should().BeTrue();
	}

	[Fact]
	public void TheStackChannelLogsOnlyCriticalLines_AndEveryOtherCategoryKeepsItsLevel()
	{
		var collector = new FakeLogCollector();
		using var provider = Build(new ServiceCollection().AddLogging(builder => builder
			.AddProvider(new FakeLoggerProvider(collector))
			.SetMinimumLevel(LogLevel.Debug)));
		var loggerFactory = provider.GetRequiredService<ILoggerFactory>();

		var channel = loggerFactory.CreateLogger(OpcUaSession.ChannelLogCategory);
		var session = loggerFactory.CreateLogger("Opc.Ua.Client.Session");

		channel.IsEnabled(LogLevel.Error).Should().BeFalse();
		channel.IsEnabled(LogLevel.Critical).Should().BeTrue();
		session.IsEnabled(LogLevel.Error).Should().BeTrue();
		session.IsEnabled(LogLevel.Debug).Should().BeTrue();
	}

	private static ServiceProvider Build(IServiceCollection services)
	{
		return services.AddOpcUaLiveSource(_settings).BuildServiceProvider();
	}
}
