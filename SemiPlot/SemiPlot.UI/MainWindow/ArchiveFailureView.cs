namespace SemiPlot.UI.MainWindow;

/// <summary>
/// One archive failure as the operator reads it: <c>Title</c> in one short line, <c>Detail</c> naming
/// what the application observed, <c>Remedy</c> naming the action that fixes it. Built with no resource
/// lookup, because it reaches the operator before any configuration, a culture included, is loaded.
/// </summary>
public sealed record ArchiveFailureView(string Title, string Detail, string Remedy);
