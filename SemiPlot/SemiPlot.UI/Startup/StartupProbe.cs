using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

using Serilog;

namespace SemiPlot.UI.Startup;

/// <summary>
/// The startup sequence that runs in <see cref="Program"/>, before Avalonia is configured: load the
/// connection file, build the container, read the pen catalogue and the archive extent.
/// </summary>
public static class StartupProbe
{
	/// <summary>The connection file's name inside <see cref="StartupOptions.ConfigDir"/>.</summary>
	public const string ConnectionFileName = "archive-connection.yaml";

	// Must stay above PostgresConnectionSettings.ConnectTimeoutSeconds, or an unreachable host reads as
	// an accepted connection that timed out.
	public static readonly TimeSpan DefaultReadBound = TimeSpan.FromSeconds(30);

	public static Result<StartupData> Run(StartupOptions options)
	{
		var settings = PostgresConnectionLoader.Load(Path.Combine(options.ConfigDir, ConnectionFileName));

		if (settings.IsFailed)
		{
			return Result.Fail<StartupData>(settings.Errors);
		}

		var serviceProvider = BuildArchiveServiceProvider(settings.Value);

		// Main runs with no SynchronizationContext ahead of BuildAvaloniaApp, so this cannot deadlock.
		return ReadAsync(serviceProvider, DefaultReadBound).GetAwaiter().GetResult();
	}

	/// <summary>The container the archive path runs on.</summary>
	internal static ServiceProvider BuildArchiveServiceProvider(PostgresConnectionSettings settings)
	{
		var services = new ServiceCollection().AddPostgresData(settings).AddUi();

		services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

		return services.BuildServiceProvider();
	}

	/// <summary>
	/// Reads the catalogue and the extent from an already-built container. A failed read or a throw is a
	/// failed <see cref="Result"/> with the container disposed, so a failed startup leaves nothing running.
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

	// WaitAsync abandons the wait, not the query: the read runs on until the provider's own backstop ends it.
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
			return Result.Fail<TValue>(new StartupReadTimedOutError(description, bound));
		}
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

	// The exception keeps its stack in the log line and reaches the operator through the mapper's
	// ExceptionalError arm.
	private static async Task<Result<StartupData>> FailAsync(ServiceProvider serviceProvider, Exception exception)
	{
		Log.Error(exception, "Startup failed before either read produced a result");

		await serviceProvider.DisposeAsync().ConfigureAwait(false);

		return Result.Fail<StartupData>(new ExceptionalError(exception.Message, exception));
	}
}
