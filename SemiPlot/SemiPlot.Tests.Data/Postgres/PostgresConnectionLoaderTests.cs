using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// Real files in a temp directory, not a mocked file system.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PostgresConnectionLoaderTests : IDisposable
{
	private const string ZoneIdentifier = "Europe/Berlin";

	private static readonly (string Field, string Value)[] _validFields =
	[
		("connection_file_version", "\"1.0\""),
		("host", "\"scada-01\""),
		("port", "5433"),
		("database", "\"semiplot_dev\""),
		("user", "\"semiplot_reader\""),
		("password", "\"s3cret\""),
		("source_time_zone", $"\"{ZoneIdentifier}\""),
		("poll_interval_ms", "1000"),
		("schema", "\"public\"")
	];

	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-connection-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public void AValidFilePopulatesEveryField()
	{
		var path = WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(path);

		Assert.True(result.IsSuccess, Describe(result));
		Assert.Equal("scada-01", result.Value.Host);
		Assert.Equal(5433, result.Value.Port);
		Assert.Equal("semiplot_dev", result.Value.Database);
		Assert.Equal("semiplot_reader", result.Value.Username);
		Assert.Equal("s3cret", result.Value.Password);
		Assert.Equal(TimeSpan.FromSeconds(1), result.Value.PollInterval);
		Assert.Equal("public", result.Value.Schema);
	}

	[Fact]
	public void AValidFileCarriesAResolvedTimeZone()
	{
		var path = WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(path);

		Assert.True(result.IsSuccess, Describe(result));
		Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(ZoneIdentifier), result.Value.SourceTimeZone);
	}

	[Fact]
	public void AnAbsentFileYieldsTheNotFoundError()
	{
		var path = Path.Combine(_directory, "archive-connection.yaml");

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileNotFoundError>());
		Assert.True(result.IsFailed);
		Assert.Equal(path, error.Path);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankPathYieldsTheNotFoundErrorRatherThanAThrow(string path)
	{
		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileNotFoundError>());
		Assert.Equal(path, error.Path);
	}

	// A directory stands in for every path that exists and cannot be read: the file is there, so telling
	// the operator the YAML is malformed would send them to fix the wrong thing.
	[Fact]
	public void APathThatCannotBeOpenedYieldsTheUnreadableDiscriminator()
	{
		var result = PostgresConnectionLoader.Load(_directory);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(_directory, error.Path);
		Assert.Equal(ConnectionFileProblem.Unreadable, error.Kind);
	}

	[Fact]
	public void AVersionMismatchYieldsTheMismatchDiscriminator()
	{
		var path = WriteFile(Compose(Replace("connection_file_version", "\"2.0\"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(path, error.Path);
		Assert.Equal(ConnectionFileProblem.VersionMismatch, error.Kind);
	}

	// A file holding nothing but the version is what pins the ordering; an otherwise-complete file cannot.
	[Fact]
	public void AVersionMismatchIsReportedAheadOfTheAbsentFields()
	{
		var path = WriteFile("connection_file_version: \"2.0\"\n");

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(ConnectionFileProblem.VersionMismatch, error.Kind);
	}

	[Fact]
	public void UnreadableYamlYieldsTheUnparseableDiscriminator()
	{
		var path = WriteFile("host: [scada-01\nport: :\n");

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(path, error.Path);
		Assert.Equal(ConnectionFileProblem.Unparseable, error.Kind);
	}

	[Fact]
	public void AnEmptyFileYieldsTheUnparseableDiscriminator()
	{
		var path = WriteFile(string.Empty);

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(ConnectionFileProblem.Unparseable, error.Kind);
	}

	[Theory]
	[InlineData("connection_file_version")]
	[InlineData("host")]
	[InlineData("port")]
	[InlineData("database")]
	[InlineData("user")]
	[InlineData("password")]
	[InlineData("source_time_zone")]
	[InlineData("poll_interval_ms")]
	[InlineData("schema")]
	public void AnAbsentRequiredFieldYieldsTheMissingFieldDiscriminator(string field)
	{
		var path = WriteFile(Compose(_validFields.Where(pair => pair.Field != field)));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(path, error.Path);
		Assert.Equal(ConnectionFileProblem.MissingField, error.Kind);
		Assert.Contains(field, error.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void ABlankRequiredFieldYieldsTheMissingFieldDiscriminator()
	{
		var path = WriteFile(Compose(Replace("host", "\"   \"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(ConnectionFileProblem.MissingField, error.Kind);
		Assert.Contains("host", error.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void AnUnknownTimeZoneYieldsTheUnknownTimeZoneDiscriminator()
	{
		var path = WriteFile(Compose(Replace("source_time_zone", "\"Mars/Olympus_Mons\"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(path, error.Path);
		Assert.Equal(ConnectionFileProblem.UnknownTimeZone, error.Kind);
		Assert.Contains("Mars/Olympus_Mons", error.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void EveryAbsentFieldIsReportedInOneError()
	{
		var absent = new[] { "host", "schema", "poll_interval_ms" };
		var path = WriteFile(Compose(_validFields.Where(pair => !absent.Contains(pair.Field))));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(ConnectionFileProblem.MissingField, error.Kind);
		Assert.All(absent, field => Assert.Contains(field, error.Reason, StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("port", "0")]
	[InlineData("port", "-1")]
	[InlineData("port", "65536")]
	[InlineData("poll_interval_ms", "0")]
	[InlineData("poll_interval_ms", "-1")]
	public void AValueOutsideItsRangeYieldsTheOutOfRangeDiscriminator(string field, string value)
	{
		var path = WriteFile(Compose(Replace(field, value)));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		Assert.Equal(path, error.Path);
		Assert.Equal(ConnectionFileProblem.OutOfRange, error.Kind);
		Assert.Contains(field, error.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("65535")]
	public void APortAtTheEdgeOfItsRangeIsAccepted(string value)
	{
		var path = WriteFile(Compose(Replace("port", value)));

		var result = PostgresConnectionLoader.Load(path);

		Assert.True(result.IsSuccess, Describe(result));
	}

	[Fact]
	public void TheFourInvalidStatesAreSeparatedByTheirDiscriminator()
	{
		var kinds = new[]
		{
			KindOf(WriteFile("host: [scada-01\n")),
			KindOf(WriteFile(Compose(_validFields.Where(pair => pair.Field != "host")))),
			KindOf(WriteFile(Compose(Replace("port", "0")))),
			KindOf(WriteFile(Compose(Replace("source_time_zone", "\"Mars/Olympus_Mons\""))))
		};

		Assert.Equal(
			[
				ConnectionFileProblem.Unparseable,
				ConnectionFileProblem.MissingField,
				ConnectionFileProblem.OutOfRange,
				ConnectionFileProblem.UnknownTimeZone
			],
			kinds);
	}

	// A parser message embeds the offending scalar, and the password is a scalar.
	[Fact]
	public void AnUnparseableFileCarriesItsCausingExceptionAndNotItsText()
	{
		var path = WriteFile("host: [scada-01\nport: :\n");

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		var caused = Assert.Single(error.Reasons.OfType<ExceptionalError>());

		Assert.NotNull(caused.Exception);
		Assert.DoesNotContain("scada-01", error.Reason, StringComparison.Ordinal);
		Assert.DoesNotContain("scada-01", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AnUnknownTimeZoneCarriesItsCausingException()
	{
		var path = WriteFile(Compose(Replace("source_time_zone", "\"Mars/Olympus_Mons\"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = Assert.Single(result.Errors.OfType<ConnectionFileInvalidError>());
		var caused = Assert.Single(error.Reasons.OfType<ExceptionalError>());

		Assert.IsType<TimeZoneNotFoundException>(caused.Exception);
	}

	[Fact]
	public void APasswordCarryingSeparatorsRoundTripsThroughTheBuilder()
	{
		const string password = "pa;ss'word";
		var path = WriteFile(Compose(Replace("password", $"\"{password}\"")));

		var result = PostgresConnectionLoader.Load(path);

		Assert.True(result.IsSuccess, Describe(result));

		var parsed = new NpgsqlConnectionStringBuilder(result.Value.ConnectionString);

		Assert.Equal(password, parsed.Password);
		Assert.Equal("scada-01", parsed.Host);
		Assert.Equal(5433, parsed.Port);
		Assert.Equal("semiplot_dev", parsed.Database);
		Assert.Equal("semiplot_reader", parsed.Username);
		Assert.Equal("public", parsed.SearchPath);
	}

	// The emitted string is asserted alongside the parsed value: the builder answers its own default for
	// a key the string never carried, so the parsed value alone proves neither absence nor presence.
	[Fact]
	public void TheConnectionStringSendsNoStatementTimeoutAndPinsAnInfiniteCommandTimeout()
	{
		var path = WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(path);

		Assert.True(result.IsSuccess, Describe(result));

		var connectionString = result.Value.ConnectionString;
		var parsed = new NpgsqlConnectionStringBuilder(connectionString);

		Assert.DoesNotContain("Options", connectionString, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("statement_timeout", connectionString, StringComparison.OrdinalIgnoreCase);
		Assert.True(string.IsNullOrEmpty(parsed.Options));
		Assert.Contains("Command Timeout=0", connectionString, StringComparison.Ordinal);
		Assert.Equal(0, parsed.CommandTimeout);
	}

	[Fact]
	public void TheSettingsNeverPrintThePassword()
	{
		const string password = "pa;ss'word";
		var path = WriteFile(Compose(Replace("password", $"\"{password}\"")));

		var result = PostgresConnectionLoader.Load(path);

		Assert.True(result.IsSuccess, Describe(result));

		var printed = result.Value.ToString();

		Assert.DoesNotContain(password, printed, StringComparison.Ordinal);
		Assert.Contains("scada-01", printed, StringComparison.Ordinal);
	}

	private static IEnumerable<(string Field, string Value)> Replace(string field, string value)
	{
		return _validFields.Select(pair => pair.Field == field ? (field, value) : pair);
	}

	private static string Compose(IEnumerable<(string Field, string Value)> fields)
	{
		return string.Join("\n", fields.Select(pair => $"{pair.Field}: {pair.Value}")) + "\n";
	}

	private static string Describe(Result<PostgresConnectionSettings> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}

	private static ConnectionFileProblem KindOf(string path)
	{
		return PostgresConnectionLoader.Load(path).Errors.OfType<ConnectionFileInvalidError>().Single().Kind;
	}

	private string WriteFile(string content)
	{
		var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.yaml");

		File.WriteAllText(path, content);

		return path;
	}
}
