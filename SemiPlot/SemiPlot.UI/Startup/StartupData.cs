using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Startup;

/// <summary>
/// Everything <see cref="StartupProbe"/> reads before Avalonia exists, carried across that boundary into
/// <see cref="App.Run(StartupData)"/>. The probe owns the reads; <c>App.InitializeServices</c> consumes
/// this record and awaits nothing, so no blocking read survives inside Avalonia's <c>AfterSetup</c>.
/// </summary>
/// <remarks>
/// <see cref="ServiceProvider"/> is the concrete container rather than <see cref="IServiceProvider"/>,
/// because the caller that receives it is also the one that disposes it.
/// </remarks>
/// <param name="HealthWarnings">
/// Faults the archive answered with that stopped nothing — a non-empty default partition today. They
/// travel beside a successful read rather than inside its <see cref="Result"/>, because a
/// <see cref="Result"/> carrying an error is a failed startup and these are not: the archive is readable
/// and the operator gets a banner over a working chart. Empty is the ordinary case, and also what a health
/// check that could not run answers.
/// </param>
public sealed record StartupData(
	ServiceProvider ServiceProvider,
	IReadOnlyList<Pen> Pens,
	ArchiveExtent Extent,
	IReadOnlyList<IError> HealthWarnings);
