namespace SemiPlot.Tests.Data.Integration;

// The availability policy for the gated tests is carried by the environment, not by a build flag:
// skipping is right on a developer machine without a container runtime and wrong in a pipeline, and
// only the caller knows which one is running.
public static class TestEnvironment
{
	// Points the fixture at an existing, semibase-provisioned server instead of starting a container.
	public const string TestServerVariable = "SEMIPLOT_TEST_PG";

	// Turns an unavailable runtime from a skip into a failure. The CI job sets it.
	public const string RequireDatabaseVariable = "SEMIPLOT_REQUIRE_DB";

	public const string ImageVariable = "SEMIPLOT_PG_IMAGE";

	public const string SemibaseExecutableVariable = "SEMIBASE_EXE";

	// SemiBase installs vanilla PostgreSQL 17 on a site, so the bench runs the same major version.
	public const string DefaultImage = "postgres:17-alpine";

	public static string? TestServerConnectionString => Read(TestServerVariable);

	public static string Image => Read(ImageVariable) ?? DefaultImage;

	public static string? SemibaseExecutable => Read(SemibaseExecutableVariable);

	public static string? WriterPassword => Read(SemibaseProvisioner.WriterPasswordVariable);

	public static string? ReaderPassword => Read(SemibaseProvisioner.ReaderPasswordVariable);

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
