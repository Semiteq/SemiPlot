using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

using Serilog;

namespace SemiPlot.UI.Startup;

/// <summary>
/// The startup sequence that runs in <see cref="Program"/>, before Avalonia is configured: load the
/// connection file, build the container, resolve <see cref="IDataProvider"/>, read the pen catalogue and
/// the archive extent. Nothing here touches an Avalonia or a ReactiveUI type, which is what lets the
/// whole sequence be driven from a test.
/// <para>
/// It exists because <c>AfterSetup</c> takes a synchronous delegate: a read left inside it either blocks
/// Avalonia's setup or throws through it. Running the reads first turns a failing archive into a
/// <see cref="Result"/> the caller branches on.
/// </para>
/// </summary>
public static class StartupProbe
{
	/// <summary>
	/// The connection file's name inside <see cref="StartupOptions.ConfigDir"/>. The directory is
	/// correctable with <c>--config-dir</c>; the file name is not.
	/// </summary>
	public const string ConnectionFileName = "archive-connection.yaml";

	/// <summary>
	/// How long each startup read may take before startup stops waiting for it. Short on purpose: a
	/// server that accepts TCP and answers nothing must not hold the splash-free startup for the
	/// provider's five-minute backstop.
	/// <para>
	/// It must stay above <see cref="PostgresConnectionSettings.ConnectTimeoutSeconds"/>, which is the
	/// invariant <c>DefaultReadBound_StaysAboveTheConnectTimeout</c> pins. An unreachable host — a
	/// wrong address, a host that is down, a firewall that drops — fails inside the connect attempt, and
	/// only a bound above that attempt lets its <c>ArchiveFault.Unreachable</c> reach the operator. Equal
	/// values race, and the operator then reads "the connection was accepted", the opposite of the truth.
	/// </para>
	/// </summary>
	public static readonly TimeSpan DefaultReadBound = TimeSpan.FromSeconds(30);

	public static Result<StartupData> Run(StartupOptions options)
	{
		return Run(options, DefaultReadBound);
	}

	public static Result<StartupData> Run(StartupOptions options, TimeSpan readBound)
	{
		ArgumentNullException.ThrowIfNull(options);

		var settings = PostgresConnectionLoader.Load(Path.Combine(options.ConfigDir, ConnectionFileName));

		if (settings.IsFailed)
		{
			return Result.Fail<StartupData>(settings.Errors);
		}

		return Read(BuildArchiveServiceProvider(settings.Value), readBound);
	}

	/// <summary>
	/// The container the archive path runs on. Internal so a composition test builds exactly what
	/// <see cref="Run(StartupOptions, TimeSpan)"/> builds, rather than a look-alike.
	/// </summary>
	internal static ServiceProvider BuildArchiveServiceProvider(PostgresConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);

		return Build(services => services.AddPostgresData(settings));
	}

	/// <summary>
	/// Reads the catalogue and the extent from an already-built container, disposing that container when
	/// either read fails so a failed startup leaves nothing running. Separate from
	/// <see cref="Run(StartupOptions, TimeSpan)"/> so a test can hand in a container holding its own
	/// <see cref="IDataProvider"/>.
	/// <para>
	/// A throw is a failed <see cref="Result"/> here, not an escape: resolving
	/// <see cref="IDataProvider"/> constructs the data source and can throw, and a cancelled read leaves
	/// the provider as an <see cref="OperationCanceledException"/> by design. Either one propagating
	/// would end the process with no window and no disposed container, which is the one outcome the
	/// operator can do nothing with.
	/// </para>
	/// </summary>
	internal static async Task<Result<StartupData>> ReadAsync(ServiceProvider serviceProvider, TimeSpan readBound)
	{
		try
		{
			var dataProvider = serviceProvider.GetRequiredService<IDataProvider>();

			var pens = await ReadBoundedAsync(dataProvider.QueryPensAsync(), readBound, "pen catalogue")
				.ConfigureAwait(false);

			if (pens.IsFailed)
			{
				return await FailAsync<StartupData>(serviceProvider, pens.Errors).ConfigureAwait(false);
			}

			var extent = await ReadBoundedAsync(dataProvider.QueryArchiveExtentAsync(), readBound, "archive extent")
				.ConfigureAwait(false);

			if (extent.IsFailed)
			{
				return await FailAsync<StartupData>(serviceProvider, extent.Errors).ConfigureAwait(false);
			}

			return Result.Ok(new StartupData(serviceProvider, pens.Value, extent.Value));
		}
		catch (Exception exception)
		{
			return await FailAsync(serviceProvider, exception).ConfigureAwait(false);
		}
	}

	private static Result<StartupData> Read(ServiceProvider serviceProvider, TimeSpan readBound)
	{
		return Task.Run(() => ReadAsync(serviceProvider, readBound)).GetAwaiter().GetResult();
	}

	// The bound is the caller's, not the provider's: IDataProvider takes no CancellationToken and does not
	// gain one here. So WaitAsync abandons the WAIT, not the QUERY — the read keeps running on its pooled
	// connection until the provider's own backstop ends it, and startup proceeds without it.
	private static async Task<Result<TValue>> ReadBoundedAsync<TValue>(
		Task<Result<TValue>> read,
		TimeSpan bound,
		string description)
	{
		try
		{
			return await read.WaitAsync(bound).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			ObserveAbandoned(read);

			return Result.Fail<TValue>(new StartupReadTimedOutError(description, bound));
		}
	}

	// Hygiene, not a crash fix: the abandoned read outlives this method and the caller disposes the data
	// source under it, so it can fault with nothing awaiting it. Nothing here sets
	// ThrowUnobservedTaskExceptions, so such a fault is raised on TaskScheduler.UnobservedTaskException and
	// swallowed. Touching Exception in a continuation marks it observed and keeps the finalizer path clear.
	private static void ObserveAbandoned<TValue>(Task<Result<TValue>> read)
	{
		_ = read.ContinueWith(
			static abandoned => _ = abandoned.Exception,
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	private static async Task<Result<TValue>> FailAsync<TValue>(
		ServiceProvider serviceProvider,
		IEnumerable<IError> errors)
	{
		var failure = Result.Fail<TValue>(errors);

		foreach (var error in failure.Errors)
		{
			Log.Error("Startup read failed: {Error}", error.Message);
		}

		await serviceProvider.DisposeAsync().ConfigureAwait(false);

		return failure;
	}

	// The exception keeps its stack in the log line and reaches the operator as an ExceptionalError, which
	// StartupFailureMapper turns into a window naming the exception type. FluentResults owns that type, so
	// the vocabulary the coverage test enumerates is unchanged.
	private static async Task<Result<StartupData>> FailAsync(ServiceProvider serviceProvider, Exception exception)
	{
		Log.Error(exception, "Startup failed before either read produced a result");

		await serviceProvider.DisposeAsync().ConfigureAwait(false);

		return Result.Fail<StartupData>(new ExceptionalError(exception.Message, exception));
	}

	private static ServiceProvider Build(Func<IServiceCollection, IServiceCollection> addDataSource)
	{
		var services = addDataSource(new ServiceCollection()).AddUi();

		services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

		return services.BuildServiceProvider();
	}
}
