using System.Reactive.Concurrency;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.UI.Startup;

// StartupProbe touches no Avalonia and no ReactiveUI type — that is the whole point of extracting it —
// so these are plain [Fact], not [AvaloniaFact].
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class StartupProbeTests
{
	private static readonly TimeSpan _shortBound = TimeSpan.FromMilliseconds(100);

	[Fact]
	public async Task ReadAsync_Succeeding_CarriesPensAndExtent()
	{
		var dataProvider = NewProvider();
		using var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.IsSuccess.Should().BeTrue();
		result.Value.Pens.Should().BeEquivalentTo(dataProvider.Pens);
		result.Value.Extent.FirstUtc.Should().Be(dataProvider.ArchiveFirstUtc);
		result.Value.Extent.LastUtc.Should().Be(dataProvider.ArchiveLastUtc);
		result.Value.ServiceProvider.Should().BeSameAs(container);
	}

	// An empty semiplot_tags is a correct answer, not a failure: the database is reachable and only
	// commissioning is unfinished. So the probe carries it out as a success and disposes nothing. What the
	// operator then sees is pinned by EmptyCatalogueStartupTests.
	[Fact]
	public async Task ReadAsync_WithAnEmptyCatalogue_SucceedsAndKeepsTheContainer()
	{
		var dataProvider = NewProvider([]);
		using var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.IsSuccess.Should().BeTrue();
		result.Errors.Should().BeEmpty();
		result.Value.Pens.Should().BeEmpty();
		container.GetRequiredService<IDataProvider>().Should().BeSameAs(dataProvider);
	}

	[Fact]
	public async Task ReadAsync_FailedCatalogue_CarriesTheErrorAndDisposesTheContainer()
	{
		var dataProvider = NewProvider();
		dataProvider.FailPens = true;
		var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ArchiveUnreachableError>();

		var resolve = () => container.GetRequiredService<IDataProvider>();
		resolve.Should().Throw<ObjectDisposedException>();
	}

	[Fact]
	public async Task ReadAsync_FailedCatalogue_DoesNotReadTheExtent()
	{
		var dataProvider = NewProvider();
		dataProvider.FailPens = true;
		dataProvider.GateExtent = true;
		var container = BuildContainer(dataProvider);

		// The extent gate is never completed: reaching it would hang the read out to its bound instead of
		// returning at once with the catalogue's error.
		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ArchiveUnreachableError>();
	}

	[Fact]
	public async Task ReadAsync_FailedExtent_CarriesTheErrorAndDisposesTheContainer()
	{
		var dataProvider = NewProvider();
		dataProvider.FailExtent = true;
		var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ArchiveReadFailedError>();

		var resolve = () => container.GetRequiredService<IDataProvider>();
		resolve.Should().Throw<ObjectDisposedException>();
	}

	[Fact]
	public async Task ReadAsync_CatalogueExceedingItsBound_FailsInsteadOfHanging()
	{
		var dataProvider = NewProvider();
		dataProvider.GatePens = true;
		var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, _shortBound);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<StartupReadTimedOutError>()
			.Which.Bound.Should().Be(_shortBound);
	}

	[Fact]
	public async Task ReadAsync_ExtentExceedingItsBound_FailsInsteadOfHanging()
	{
		var dataProvider = NewProvider();
		dataProvider.GateExtent = true;
		var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, _shortBound);

		result.Errors.Should().ContainSingle().Which.Should().BeOfType<StartupReadTimedOutError>()
			.Which.Read.Should().Be("archive extent");
	}

	// The bound and Npgsql's connect timeout race whenever they are equal, and the loser decides what the
	// operator reads: a bound that expires first reports a startup timeout whose remedy states the
	// connection was accepted, on every unreachable host. The ordering is the contract, so it is pinned
	// against the connection string the settings actually yield, not against a literal.
	[Fact]
	public void DefaultReadBound_StaysAboveTheConnectTimeout()
	{
		var connectionString = new NpgsqlConnectionStringBuilder(UnreachableSettings().ConnectionString);

		StartupProbe.DefaultReadBound.TotalSeconds.Should().BeGreaterThan(connectionString.Timeout);
	}

	// Resolving IDataProvider builds the NpgsqlDataSource and can throw, and ArchiveExceptionMapper
	// rethrows OperationCanceledException rather than mapping it. Either one escaping ends the process
	// with no window and an undisposed container.
	[Fact]
	public async Task ReadAsync_ResolvingTheProviderThrowing_FailsInsteadOfPropagating()
	{
		var services =
			new ServiceCollection()
				.AddSingleton<IScheduler>(CurrentThreadScheduler.Instance)
				.AddSingleton<IDataProvider>(_ => throw new InvalidOperationException("no data source"))
				.AddUi();

		services.AddLogging();
		var container = services.BuildServiceProvider();

		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeAssignableTo<IExceptionalError>()
			.Which.Exception.Should().BeOfType<InvalidOperationException>();

		var resolve = () => container.GetRequiredService<IScheduler>();
		resolve.Should().Throw<ObjectDisposedException>();
	}

	[Fact]
	public async Task ReadAsync_ReadThrowing_FailsInsteadOfPropagating()
	{
		var dataProvider = NewProvider();
		dataProvider.GatePens = true;
		dataProvider.PensGate.SetException(new OperationCanceledException("the read was cancelled"));
		var container = BuildContainer(dataProvider);

		var result = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeAssignableTo<IExceptionalError>()
			.Which.Exception.Should().BeOfType<OperationCanceledException>();

		var resolve = () => container.GetRequiredService<IDataProvider>();
		resolve.Should().Throw<ObjectDisposedException>();
	}

	[Fact]
	public void Run_OverTheStubContainer_CarriesPensAndExtent()
	{
		var result = StartupProbe.Run(StartupOptions.Parse(["--use-stub"]), StartupProbe.DefaultReadBound);

		result.IsSuccess.Should().BeTrue();

		using var container = result.Value.ServiceProvider;

		result.Value.Pens.Should().NotBeEmpty();
		result.Value.Extent.IsEmpty.Should().BeFalse();
		container.GetRequiredService<IDataProvider>().Should().NotBeNull();
	}

	// The archive path reads the connection file before it builds anything, and a missing file ends
	// startup there. It must never fall back to the stub — synthetic numbers read as process data is the
	// failure the flag-only stub exists to prevent.
	[Fact]
	public void Run_WithNoConnectionFile_FailsInsteadOfFallingBackToTheStub()
	{
		var emptyConfigDir = Path.Combine(Path.GetTempPath(), $"semiplot-probe-{Guid.NewGuid():N}");

		var result = StartupProbe.Run(
			StartupOptions.Parse(["--config-dir", emptyConfigDir]),
			StartupProbe.DefaultReadBound);

		result.IsFailed.Should().BeTrue();
		result.Errors.Should().ContainSingle().Which.Should().BeOfType<ConnectionFileNotFoundError>()
			.Which.Path.Should().Be(Path.Combine(emptyConfigDir, StartupProbe.ConnectionFileName));
	}

	// A local copy of the SemiPlot.Tests.Data equivalent rather than a reference between two test
	// projects: port 1 answers nowhere, and nothing here opens a connection.
	private static PostgresConnectionSettings UnreachableSettings()
	{
		return new PostgresConnectionSettings(
			Host: "127.0.0.1",
			Port: 1,
			Database: "semiplot_dev",
			Username: "semiplot_reader",
			Password: "unused",
			SourceTimeZone: TimeZoneInfo.Utc,
			PollInterval: TimeSpan.FromSeconds(1),
			Schema: "public");
	}

	private static FakeDataProvider NewProvider(IReadOnlyList<Pen>? pens = null)
	{
		return new FakeDataProvider(CurrentThreadScheduler.Instance, TimeSpan.FromSeconds(1), pens);
	}

	private static ServiceProvider BuildContainer(IDataProvider dataProvider)
	{
		var services =
			new ServiceCollection()
				.AddSingleton<IScheduler>(CurrentThreadScheduler.Instance)
				.AddSingleton(dataProvider)
				.AddUi();

		services.AddLogging();

		return services.BuildServiceProvider();
	}
}
