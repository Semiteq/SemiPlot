using AwesomeAssertions;

using FluentResults;

using Npgsql;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

using YamlDotNet.Core;

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
		("poll_interval_ms", "1000"),
		("schema", "\"public\"")
	];

	private readonly string _directory = Directory.CreateTempSubdirectory("semiplot-connection-").FullName;

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
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
		result.Value.PollInterval.Should().Be(TimeSpan.FromSeconds(1));
		result.Value.Schema.Should().Be("public");
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
	public void TwoFilesOfTheSectionAreMergedIntoOneSettings()
	{
		WriteFile(Compose(_validFields.Take(4)), "a.yaml");
		WriteFile(Compose(_validFields.Skip(4)), "b.yaml");

		var result = PostgresConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("10.20.30.40");
		result.Value.Password.Should().Be("s3cret");
	}

	[Fact]
	public void AnAbsentSectionYieldsASectionErrorRatherThanAThrow()
	{
		var absent = Path.Combine(_directory, "connection");

		var result = PostgresConnectionLoader.Load(absent);

		var error = SectionErrorOf(result);
		error.Problem.Should().Be(SectionProblem.DirectoryMissing);
		error.Section.Should().Be(ConfigurationSectionName.Connection);
		error.Directory.Should().Be(absent);
	}

	[Fact]
	public void ASectionWithNoFileYieldsTheNoFilesProblem()
	{
		var result = PostgresConnectionLoader.Load(_directory);

		SectionErrorOf(result).Problem.Should().Be(SectionProblem.NoFiles);
	}

	[Fact]
	public void AFieldCarriedByTwoFilesYieldsTheKeyConflictProblem()
	{
		WriteFile(Compose(_validFields), "a.yaml");
		WriteFile("host: \"10.20.30.41\"\n", "b.yaml");

		var result = PostgresConnectionLoader.Load(_directory);

		var error = SectionErrorOf(result);
		error.Problem.Should().Be(SectionProblem.KeyConflict);
		error.Key.Should().Be("host");
		error.FileNames.Should().Equal("a.yaml", "b.yaml");
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void ABlankPathYieldsASectionErrorRatherThanAThrow(string path)
	{
		var result = PostgresConnectionLoader.Load(path);

		SectionErrorOf(result).Problem.Should().Be(SectionProblem.DirectoryMissing);
	}

	[Fact]
	public void UnreadableYamlYieldsTheSectionUnreadableProblem()
	{
		WriteFile("host: [scada-01\nport: :\n");

		var result = PostgresConnectionLoader.Load(_directory);

		var error = SectionErrorOf(result);
		error.Problem.Should().Be(SectionProblem.Unreadable);
		error.FileNames.Should().Equal("connection.yaml");
	}

	// The merged text is well-formed YAML; a value the DTO's own type cannot take is what still throws here.
	[Fact]
	public void AFieldWhoseValueTheFormatRejectsYieldsTheUnparseableDiscriminator()
	{
		WriteFile(Compose(Replace("port", "\"not-a-number\"")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.Unparseable);
	}

	[Fact]
	public void AnEmptyFileYieldsTheMissingFieldDiscriminator()
	{
		WriteFile(string.Empty);

		var result = PostgresConnectionLoader.Load(_directory);

		ErrorOf(result).Kind.Should().Be(ConnectionFileProblem.MissingField);
	}

	[Theory]
	[InlineData("host")]
	[InlineData("port")]
	[InlineData("database")]
	[InlineData("user")]
	[InlineData("password")]
	[InlineData("poll_interval_ms")]
	public void AnAbsentRequiredFieldYieldsTheMissingFieldDiscriminator(string field)
	{
		WriteFile(Compose(_validFields.Where(pair => pair.Field != field)));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Reason.Should().Contain(field);
	}

	[Fact]
	public void ABlankRequiredFieldYieldsTheMissingFieldDiscriminator()
	{
		WriteFile(Compose(Replace("host", "\"   \"")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Kind.Should().Be(ConnectionFileProblem.MissingField);
		error.Reason.Should().Contain("host");
	}

	[Theory]
	[InlineData("0.0.0.0")]
	[InlineData("127.0.0.1")]
	[InlineData("192.168.0.10")]
	[InlineData("255.255.255.255")]
	public void AnIPv4AddressOfFourDecimalOctetsIsAHost(string host)
	{
		PostgresConnectionLoader.IsIPv4Address(host).Should().BeTrue();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("localhost")]
	[InlineData("::1")]
	[InlineData("::ffff:127.0.0.1")]
	[InlineData("127.1")]
	[InlineData("1.2.3.4.5")]
	[InlineData("1..2.3")]
	[InlineData("256.0.0.1")]
	[InlineData("1.2.3.1000")]
	[InlineData("010.0.0.1")]
	[InlineData("+1.2.3.4")]
	[InlineData(" 127.0.0.1")]
	[InlineData("\uFF11.2.3.4")]
	public void AnythingButFourDecimalOctetsIsNotAHost(string? host)
	{
		PostgresConnectionLoader.IsIPv4Address(host).Should().BeFalse();
	}

	[Fact]
	public void AHostThatIsNotAnIPv4AddressYieldsTheHostNotIPv4Discriminator()
	{
		WriteFile(Compose(Replace("host", "scada-01")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.HostNotIPv4);
		error.Reason.Should().Contain("host");
	}

	[Fact]
	public void EveryAbsentFieldIsReportedInOneError()
	{
		var absent = new[] { "host", "poll_interval_ms" };
		WriteFile(Compose(_validFields.Where(pair => !absent.Contains(pair.Field))));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
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
		WriteFile(Compose(Replace(field, value)));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(ConnectionFileProblem.OutOfRange);
		error.Reason.Should().Contain(field);
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

	[Fact]
	public void TheThreeInvalidStatesAreSeparatedByTheirDiscriminator()
	{
		var kinds = new[]
		{
			KindOf(Compose(_validFields.Where(pair => pair.Field != "host"))),
			KindOf(Compose(Replace("port", "0"))),
			KindOf(Compose(Replace("host", "scada-01")))
		};

		kinds.Should().Equal(
			[
				ConnectionFileProblem.MissingField,
				ConnectionFileProblem.OutOfRange,
				ConnectionFileProblem.HostNotIPv4
			]);
	}

	// A parser message embeds the offending scalar, and the password is a scalar.
	[Fact]
	public void AnUnparseableSectionCarriesItsCausingExceptionAndNotItsText()
	{
		WriteFile(Compose(Replace("port", "\"scada-01\"")));

		var result = PostgresConnectionLoader.Load(_directory);

		var error = ErrorOf(result);
		var caused = error.Reasons.OfType<ExceptionalError>().Should().ContainSingle().Which;

		caused.Exception.Should().BeAssignableTo<YamlException>();
		error.Reason.Should().NotContain("scada-01");
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
	public void TheConnectionStringSendsNoStatementTimeoutAndCarriesTheClientBackstop()
	{
		WriteFile(Compose(_validFields));

		var result = PostgresConnectionLoader.Load(_directory);

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

	private static string Describe(Result<PostgresConnectionSettings> result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}

	private static ConnectionFileError ErrorOf(Result<PostgresConnectionSettings> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<ConnectionFileError>().Should().ContainSingle().Which;
	}

	private static ConfigurationSectionError SectionErrorOf(Result<PostgresConnectionSettings> result)
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<ConfigurationSectionError>().Should().ContainSingle().Which;
	}

	private static ConnectionFileProblem KindOf(string content)
	{
		var section = Directory.CreateTempSubdirectory("semiplot-connection-kind-").FullName;

		File.WriteAllText(Path.Combine(section, "connection.yaml"), content);

		try
		{
			return PostgresConnectionLoader.Load(section).Errors.OfType<ConnectionFileError>().Single().Kind;
		}
		finally
		{
			Directory.Delete(section, recursive: true);
		}
	}

	private void WriteFile(string content, string name = "connection.yaml")
	{
		File.WriteAllText(Path.Combine(_directory, name), content);
	}
}
