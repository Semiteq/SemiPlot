using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.OpcUa.Configuration;

namespace SemiPlot.DataSource.OpcUa;

public static class OpcUaDataServiceCollectionExtensions
{
	/// <summary>Registers <see cref="OpcUaLiveSource"/> as the live source; it opens nothing on its own.</summary>
	public static IServiceCollection AddOpcUaLiveSource(
		this IServiceCollection services,
		OpcUaConnectionSettings settings)
	{
		services.AddLogging(builder => builder.AddFilter(OpcUaSession.ChannelLogCategory, LogLevel.Critical));

		// A factory, not type activation: the constructor is internal.
		services.AddSingleton<ILiveSource>(provider => new OpcUaLiveSource(
			settings,
			provider.GetRequiredService<ILoggerFactory>(),
			OpcUaLiveSource.Grace));

		return services;
	}
}
