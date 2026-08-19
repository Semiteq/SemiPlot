using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Everything <see cref="StartupProbe"/> reads before Avalonia exists, carried across that boundary into
/// <see cref="App.Run(StartupData)"/>. The probe owns the reads; <c>App.InitializeServices</c> consumes
/// this record and awaits nothing, so no blocking read survives inside Avalonia's <c>AfterSetup</c>.
/// </summary>
/// <remarks>
/// <see cref="ServiceProvider"/> is the concrete container rather than <see cref="IServiceProvider"/>,
/// because the caller that receives it is also the one that disposes it.
/// <para>
/// <see cref="Settings"/> is null on the <c>--use-stub</c> path only, where no connection file is read.
/// It is not a fallback: the archive path either loads the file or fails startup outright.
/// </para>
/// </remarks>
public sealed record StartupData(
	ServiceProvider ServiceProvider,
	IReadOnlyList<Pen> Pens,
	ArchiveExtent Extent,
	PostgresConnectionSettings? Settings);
