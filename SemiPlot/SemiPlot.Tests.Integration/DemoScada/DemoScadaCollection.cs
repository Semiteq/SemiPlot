using Xunit;

namespace SemiPlot.Tests.Integration.DemoScada;

// Every demo-server test joins this collection so no two servers start side by side.
[CollectionDefinition(Name)]
public sealed class DemoScadaCollection
{
	public const string Name = "demo-scada";
}
