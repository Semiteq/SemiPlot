using FluentResults;

namespace SemiPlot.Tests.Data.Integration;

// What both gated read-test classes share beyond the provider itself.
public static class ArchiveReadSupport
{
	// Both read-test classes drive the catalogue through these two states, so they live here rather than in
	// whichever file happened to need them first.
	public const string EmptyCatalogCommand = "DELETE FROM public.semiplot_tags;";

	public const string DropCatalogCommand = "DROP TABLE public.semiplot_tags;";

	// A failed Result's messages, so an assertion failure names the archive state rather than only the
	// expectation it broke.
	public static string Describe<T>(Result<T> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}
}
