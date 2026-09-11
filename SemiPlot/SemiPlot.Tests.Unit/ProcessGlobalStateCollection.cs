using Xunit;

namespace SemiPlot.Tests.Unit;

// The UI culture and the theme variant are process-global, and every class that writes one or reads
// what it selects joins this collection: docs/architecture/testing-strategy.md, Unit tests.
[CollectionDefinition(Name)]
public sealed class ProcessGlobalStateCollection
{
	public const string Name = "process-global-state";
}
