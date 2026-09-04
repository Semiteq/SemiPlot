using AwesomeAssertions;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// Real files in a temp directory, not a mocked file system.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PostgresConnectionLoaderTests : IDisposable
{
	private const string ZoneIdentifier = "Europe/Berlin";

	private static readonly (string Field, string Value)[] _validFields =
	[
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

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("scada-01");
		result.Value.Port.Should().Be(5433);
		result.Value.Database.Should().Be("semiplot_dev");
		result.Value.Username.Should().Be("semiplot_reader");
		result.Value.Password.Should().Be("s3cret");
		result.Value.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
		result.Value.Schema.Should().Be("public");
	}

	[Fact]
	public void AValidFileCarriesAResolvedTimeZone()
	{
		var path = WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.SourceTimeZone.Should().Be(TimeZoneInfo.FindSystemTimeZoneById(ZoneIdentifier));
	}

	[Fact]
	public void ASchemaFieldAbsentDefaultsToPublic()
	{
		var path = WriteFile(Compose(_validFields.Where(pair => pair.Field != "schema")));

		var result = PostgresConnectionLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Schema.Should().Be("public");
	}

	[Fact]
	public void AnAbsentFileYieldsTheNotFoundError()
	{
		var path = Path.Combine(_directory, "archive-connection.yaml");

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		result.IsFailed.Should().BeTrue();
		error.Kind.Should().Be(ConnectionFileProblem.NotFound);
		error.Path.Should().Be(path);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankPathYieldsTheNotFoundErrorRatherThanAThrow(string path)
	{
		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Kind.Should().Be(ConnectionFileProblem.NotFound);
		error.Path.Should().Be(path);
	}

	// A directory stands in for every path that exists and cannot be read: the file is there, so telling
	// the operator the YAML is malformed would send them to fix the wrong thing.
	[Fact]
	public void APathThatCannotBeOpenedYieldsTheUnreadableDiscriminator()
	{
		var result = PostgresConnectionLoader.Load(_directory);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.Unreadable);
	}

	[Fact]
	public void UnreadableYamlYieldsTheUnparseableDiscriminator()
	{
		var path = WriteFile("host: [scada-01\nport: :\n");

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Path.Should().Be(path);
		error.Kind.Should().Be(ConnectionFileProblem.Unparseable);
	}

	[Fact]
	public void AnEmptyFileYieldsTheUnparseableDiscriminator()
	{
		var path = WriteFile(string.Empty);

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Kind.Should().Be(ConnectionFileProblem.Unparseable);
	}

	[Theory]
	[InlineData("host")]
	[InlineData("port")]
	[InlineData("database")]
	[InlineData("user")]
	[InlineData("password")]
	[InlineData("source_time_zone")]
	[InlineData("poll_interval_ms")]
	public void AnAbsentRequiredFieldYieldsTheMissingFieldDiscriminator(string field)
	{
		var path = WriteFile(Compose(_validFields.Where(pair => pair.Field != field)));

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Path.Should().Be(path);
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Reason.Should().Contain(field);
	}

	[Fact]
	public void ABlankRequiredFieldYieldsTheMissingFieldDiscriminator()
	{
		var path = WriteFile(Compose(Replace("host", "\"   \"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Reason.Should().Contain("host");
	}

	[Fact]
	public void AnUnknownTimeZoneYieldsTheUnknownTimeZoneDiscriminator()
	{
		var path = WriteFile(Compose(Replace("source_time_zone", "\"Mars/Olympus_Mons\"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Path.Should().Be(path);
		error.Kind.Should().Be(ConnectionFileProblem.UnknownTimeZone);
		error.Reason.Should().Contain("Mars/Olympus_Mons");
	}

	[Fact]
	public void EveryAbsentFieldIsReportedInOneError()
	{
		var absent = new[] { "host", "poll_interval_ms" };
		var path = WriteFile(Compose(_validFields.Where(pair => !absent.Contains(pair.Field))));

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		absent.Should().AllSatisfy(field => error.Reason.Should().Contain(field));
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

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		error.Path.Should().Be(path);
		error.Kind.Should().Be(ConnectionFileProblem.OutOfRange);
		error.Reason.Should().Contain(field);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("65535")]
	public void APortAtTheEdgeOfItsRangeIsAccepted(string value)
	{
		var path = WriteFile(Compose(Replace("port", value)));

		var result = PostgresConnectionLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));
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

		kinds.Should().Equal(
			[
				ConnectionFileProblem.Unparseable,
				ConnectionFileProblem.MissingField,
				ConnectionFileProblem.OutOfRange,
				ConnectionFileProblem.UnknownTimeZone
			]);
	}

	// A parser message embeds the offending scalar, and the password is a scalar.
	[Fact]
	public void AnUnparseableFileCarriesItsCausingExceptionAndNotItsText()
	{
		var path = WriteFile("host: [scada-01\nport: :\n");

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		var caused = error.Reasons.OfType<ExceptionalError>().Should().ContainSingle().Which;

		caused.Exception.Should().NotBeNull();
		error.Reason.Should().NotContain("scada-01");
		error.Message.Should().NotContain("scada-01");
	}

	[Fact]
	public void AnUnknownTimeZoneCarriesItsCausingException()
	{
		var path = WriteFile(Compose(Replace("source_time_zone", "\"Mars/Olympus_Mons\"")));

		var result = PostgresConnectionLoader.Load(path);

		var error = result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
		var caused = error.Reasons.OfType<ExceptionalError>().Should().ContainSingle().Which;

		caused.Exception.Should().BeOfType<TimeZoneNotFoundException>();
	}

	[Fact]
	public void APasswordCarryingSeparatorsRoundTripsThroughTheBuilder()
	{
		const string password = "pa;ss'word";
		var path = WriteFile(Compose(Replace("password", $"\"{password}\"")));

		var result = PostgresConnectionLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var parsed = new NpgsqlConnectionStringBuilder(result.Value.ConnectionString);

		parsed.Password.Should().Be(password);
		parsed.Host.Should().Be("scada-01");
		parsed.Port.Should().Be(5433);
		parsed.Database.Should().Be("semiplot_dev");
		parsed.Username.Should().Be("semiplot_reader");
		parsed.SearchPath.Should().Be("public");
	}

	// The emitted string is asserted alongside the parsed value: the builder answers its own default for
	// a key the string never carried, so the parsed value alone proves neither absence nor presence.
	[Fact]
	public void TheConnectionStringSendsNoStatementTimeoutAndCarriesTheClientBackstop()
	{
		var path = WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var connectionString = result.Value.ConnectionString;
		var parsed = new NpgsqlConnectionStringBuilder(connectionString);

		connectionString.Should().NotContainEquivalentOf("Options");
		connectionString.Should().NotContainEquivalentOf("statement_timeout");
		string.IsNullOrEmpty(parsed.Options).Should().BeTrue();
		connectionString.Should().Contain("Command Timeout=300");
		parsed.CommandTimeout.Should().Be(PostgresConnectionSettings.CommandTimeoutSeconds);
	}

	[Fact]
	public void TheSettingsNeverPrintThePassword()
	{
		const string password = "pa;ss'word";
		var path = WriteFile(Compose(Replace("password", $"\"{password}\"")));

		var result = PostgresConnectionLoader.Load(path);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var printed = result.Value.ToString();

		printed.Should().NotContain(password);
		printed.Should().Contain("scada-01");
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
		return PostgresConnectionLoader.Load(path).Errors.OfType<ConnectionFileError>().Single().Kind;
	}

	private string WriteFile(string content)
	{
		var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.yaml");

		File.WriteAllText(path, content);

		return path;
	}
}
