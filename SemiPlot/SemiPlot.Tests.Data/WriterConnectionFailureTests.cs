using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data;

// A connection the writers cannot make surfaces as the exception Npgsql raises, which the entry point
// prints as one line. Neither case needs a server: a malformed string never leaves the Npgsql
// constructor, and a closed port is refused at once.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class WriterConnectionFailureTests
{
	// Port 1 on the loopback holds no listener, so the refusal arrives without waiting out a timeout.
	private const string ClosedPort =
		"Host=127.0.0.1;Port=1;Database=archive;Username=scada_writer;Timeout=2;Command Timeout=2";

	private const string Malformed = "nonsense=1";

	private static readonly DateTime _start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _end = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

	[Theory]
	[InlineData(ClosedPort)]
	[InlineData(Malformed)]
	public async Task TheArchiveWriterReportsAConnectionItCannotMake(string connectionString)
	{
		var thrown = await Assert.ThrowsAnyAsync<Exception>(() => new ArchiveWriter(connectionString)
			.WriteAsync([], _start, _end, cancellationToken: TestContext.Current.CancellationToken));

		Assert.True(thrown is NpgsqlException or ArgumentException or FormatException, thrown.GetType().Name);
	}

	[Theory]
	[InlineData(ClosedPort)]
	[InlineData(Malformed)]
	public async Task TheTagCatalogWriterReportsAConnectionItCannotMake(string connectionString)
	{
		var thrown = await Assert.ThrowsAnyAsync<Exception>(() => new TagCatalogWriter(connectionString)
			.WriteAsync(RawLayerGenerator.SelectPens(1), TestContext.Current.CancellationToken));

		Assert.True(thrown is NpgsqlException or ArgumentException or FormatException, thrown.GetType().Name);
	}
}
