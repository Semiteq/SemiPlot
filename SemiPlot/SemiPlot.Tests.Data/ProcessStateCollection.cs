using Xunit;

namespace SemiPlot.Tests.Data;

// Tests that reach process-wide state — an environment variable, PATH, the console writers — join this
// collection. Parallelisation is disabled for it, because such state is shared with every other test in
// flight and a value restored a moment later is still wrong while it is set.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateCollection
{
	public const string Name = "process-state";
}
