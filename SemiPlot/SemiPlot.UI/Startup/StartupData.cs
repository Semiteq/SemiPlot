using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Everything <see cref="StartupProbe"/> reads before Avalonia exists, carried across that boundary into
/// <see cref="App.Run(FluentResults.Result{StartupData})"/>.
/// </summary>
/// <remarks>
/// <see cref="ServiceProvider"/> is the concrete container rather than <see cref="IServiceProvider"/>,
/// because the caller that receives it is also the one that disposes it.
/// </remarks>
public sealed record StartupData(
	ServiceProvider ServiceProvider,
	IReadOnlyList<Pen> Pens,
	ArchiveExtent Extent);
