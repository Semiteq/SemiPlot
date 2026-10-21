using AwesomeAssertions;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

using YamlDotNet.Core;

using static SemiPlot.Tests.Unit.ResultText;

namespace SemiPlot.Tests.Unit.Postgres;

// Real files in a temp directory, not a mocked file system.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PostgresConnectionLoaderTests : IDisposable
{
	private static readonly (string Field, string Value)[] _validFields =
	[
		("host", "\"10.20.30.40\""),
		("port", "5433"),
		("database", "\"semiplot_dev\""),
		("user", "\"semiplot_reader\""),
		("password", "\"s3cret\""),
		("schema", "\"public\"")
	];

	private readonly TemporaryDirectory _directory = new("semiplot-connection");

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Fact]
	public void AValidSectionPopulatesEveryField()
	{
		WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("10.20.30.40");
		result.Value.Port.Should().Be(5433);
		result.Value.Database.Should().Be("semiplot_dev");
		result.Value.Username.Should().Be("semiplot_reader");
		result.Value.Password.Should().Be("s3cret");
		result.Value.Schema.Should().Be("public");
	}

	// An upgraded site keeps its installed connection file, which still carries the retired poll period.
	[Fact]
	public void ALeftoverPollIntervalKeyIsIgnored()
	{
		WriteFile(Compose(_validFields.Append(("poll_interval_ms", "1000"))));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Port.Should().Be(5433);
	}

	[Fact]
	public void AValidSectionCarriesTheMachinesTimeZone()
	{
		WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.SourceTimeZone.Should().Be(TimeZoneInfo.Local);
	}

	[Fact]
	public void ALeftoverTimeZoneKeyIsIgnored()
	{
		WriteFile(Compose(_validFields.Append(("source_time_zone", "\"Mars/Olympus_Mons\""))));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.SourceTimeZone.Should().Be(TimeZoneInfo.Local);
	}

	[Fact]
	public void ASchemaFieldAbsentDefaultsToPublic()
	{
		WriteFile(Compose(_validFields.Where(pair => pair.Field != "schema")));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Schema.Should().Be("public");
	}

	[Fact]
	public void AnAbsentSectionYieldsASectionErrorRatherThanAThrow()
	{
		var absent = Path.Combine(_directory, "connection");

		var result = PostgresConnectionLoader.Load(absent);

		var error = ErrorOf<ConfigurationSectionError>(result);
		error.Problem.Should().Be(SectionProblem.DirectoryMissing);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Directory.Should().Be(absent);
	}

	// The merged text is well-formed YAML; a value the DTO's own type cannot take is what still throws here.
	[Fact]
	public void AFieldWhoseValueTheFormatRejectsYieldsTheUnparseableDiscriminator()
	{
		WriteFile(Compose(Replace("port", "\"not-a-number\"")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.Unparseable);
	}

	[Fact]
	public void AnEmptyFileYieldsTheMissingFieldDiscriminator()
	{
		WriteFile(string.Empty);

		var result = PostgresConnectionLoader.Load(_directory);

		ErrorOf<ConnectionFileError>(result).Kind.Should().Be(ConnectionFileProblem.MissingField);
	}

	[Theory]
	[InlineData("host")]
	[InlineData("port")]
	[InlineData("database")]
	[InlineData("user")]
	[InlineData("password")]
	public void AnAbsentRequiredFieldYieldsTheMissingFieldDiscriminator(string field)
	{
		WriteFile(Compose(_validFields.Where(pair => pair.Field != field)));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Fields.Should().Equal(field);
	}

	[Fact]
	public void ABlankRequiredFieldYieldsTheMissingFieldDiscriminator()
	{
		WriteFile(Compose(Replace("host", "\"   \"")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Fields.Should().Equal("host");
	}

	[Fact]
	public void AHostThatIsNotAnIPv4AddressYieldsTheHostNotIPv4Discriminator()
	{
		WriteFile(Compose(Replace("host", "scada-01")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.HostNotIPv4);
		error.Fields.Should().Equal("host");
	}

	[Fact]
	public void EveryAbsentFieldIsReportedInOneError()
	{
		var absent = new[] { "host", "port" };
		WriteFile(Compose(_validFields.Where(pair => !absent.Contains(pair.Field))));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Fields.Should().Equal(absent);
	}

	[Theory]
	[InlineData("port", "0")]
	[InlineData("port", "-1")]
	[InlineData("port", "65536")]
	public void AValueOutsideItsRangeYieldsTheOutOfRangeDiscriminator(string field, string value)
	{
		WriteFile(Compose(Replace(field, value)));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.OutOfRange);
		error.Fields.Should().Equal(field);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("65535")]
	public void APortAtTheEdgeOfItsRangeIsAccepted(string value)
	{
		WriteFile(Compose(Replace("port", value)));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
	}

	// A parser message embeds the offending scalar, and the password is a scalar.
	[Fact]
	public void AnUnparseableSectionCarriesItsCausingExceptionAndNotItsText()
	{
		WriteFile(Compose(Replace("port", "\"scada-01\"")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf<ConnectionFileError>(result);
		var caused = error.Reasons.OfType<ExceptionalError>().Should().ContainSingle().Which;

		caused.Exception.Should().BeAssignableTo<YamlException>();
		error.Fields.Should().BeEmpty();
		error.Message.Should().NotContain("scada-01");
	}

	[Fact]
	public void APasswordCarryingSeparatorsRoundTripsThroughTheBuilder()
	{
		const string Password = "pa;ss'word";
		WriteFile(Compose(Replace("password", $"\"{Password}\"")));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var parsed = new NpgsqlConnectionStringBuilder(result.Value.ConnectionString);

		parsed.Password.Should().Be(Password);
		parsed.Host.Should().Be("10.20.30.40");
		parsed.Port.Should().Be(5433);
		parsed.Database.Should().Be("semiplot_dev");
		parsed.Username.Should().Be("semiplot_reader");
		parsed.SearchPath.Should().Be("public");
	}

	// The emitted string is asserted alongside the parsed value: the builder answers its own default for
	// a key the string never carried, so the parsed value alone proves neither absence nor presence.
	[Fact]
	public void TheConnectionStringTurnsJitOffAndSendsNoStatementTimeout()
	{
		WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var connectionString = result.Value.ConnectionString;
		var parsed = new NpgsqlConnectionStringBuilder(connectionString);

		connectionString.Should().Contain("jit=off");
		connectionString.Should().NotContainEquivalentOf("statement_timeout");
		parsed.Options.Should().Be("-c jit=off");
		connectionString.Should().Contain("Command Timeout=300");
		parsed.CommandTimeout.Should().Be(PostgresConnectionSettings.CommandTimeoutSeconds);
	}

	[Fact]
	public void TheSettingsNeverPrintThePassword()
	{
		const string Password = "pa;ss'word";
		WriteFile(Compose(Replace("password", $"\"{Password}\"")));

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));

		var printed = result.Value.ToString();

		printed.Should().NotContain(Password);
		printed.Should().Contain("10.20.30.40");
	}

	private static IEnumerable<(string Field, string Value)> Replace(string field, string value)
	{
		return _validFields.Select(pair => pair.Field == field ? (field, value) : pair);
	}

	private static string Compose(IEnumerable<(string Field, string Value)> fields)
	{
		return string.Join("\n", fields.Select(pair => $"{pair.Field}: {pair.Value}")) + "\n";
	}

	private void WriteFile(string content, string name = "connection.yaml")
	{
		File.WriteAllText(Path.Combine(_directory, name), content);
	}
}
