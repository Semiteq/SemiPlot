namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// A refusal the operator can act on: the archive is not what the run needs. The entry point prints the
/// message and exits 1; anything else that escapes is a fault in this tool and keeps its stack trace.
/// </summary>
public sealed class SeederException(string message) : Exception(message);
