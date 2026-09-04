using FluentResults;

namespace SemiPlot.Tests.Integration;

public static class ArchiveReadSupport
{
	public const string EmptyCatalogCommand = "DELETE FROM public.semiplot_tags;";

	public const string DropCatalogCommand = "DROP TABLE public.semiplot_tags;";

	// Provisioning creates public.trends, so "provisioned, catalogue present, archive absent" is
	// reached by cloning the provisioned source and dropping the table again. Issued as scada_writer,
	// which owns it.
	public const string DropTrendsCommand = "DROP TABLE public.trends;";

	// A failed Result's messages, so an assertion failure names the archive state rather than only the
	// expectation it broke. ResultBase rather than Result<T>, because the guards that answer with a bare
	// Result report their failures the same way.
	public static string Describe(ResultBase result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}
}
