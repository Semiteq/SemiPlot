namespace SemiPlot.Tests.Data.Integration;

public static class TestEnvironment
{
	// Turns an unavailable runtime from a skip into a failure. The CI job sets it.
	public const string RequireDatabaseVariable = "SEMIPLOT_REQUIRE_DB";

	public const string ImageVariable = "SEMIPLOT_PG_IMAGE";

	// SemiBase installs vanilla PostgreSQL 17 on a site, so the bench runs the same major version.
	public const string DefaultImage = "postgres:17-alpine";

	public static string Image => Read(ImageVariable) ?? DefaultImage;

	public static bool DatabaseRequired
	{
		get
		{
			var value = Read(RequireDatabaseVariable);

			return string.Equals(value, "1", StringComparison.Ordinal)
				|| string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
		}
	}

	private static string? Read(string name)
	{
		var value = Environment.GetEnvironmentVariable(name);

		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}
}
