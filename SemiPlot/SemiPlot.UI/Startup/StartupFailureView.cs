namespace SemiPlot.UI.Startup;

/// <summary>
/// One startup failure as the operator reads it: what broke, what the application saw, and what to do
/// about it. Every string is English and built without a resource lookup, because the window that shows
/// it opens before any configuration — including a culture — has been loaded.
/// </summary>
/// <param name="Title">The failure in one short line, the window's heading.</param>
/// <param name="Detail">What the application observed, naming the host, path or object involved.</param>
/// <param name="Remedy">The action that fixes it. Distinct per error type — that is why the vocabulary
/// has more than one type.</param>
public sealed record StartupFailureView(string Title, string Detail, string Remedy);
