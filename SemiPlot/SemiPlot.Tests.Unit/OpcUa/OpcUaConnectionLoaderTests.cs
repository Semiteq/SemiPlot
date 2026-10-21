using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Configuration;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa.Configuration;

using Xunit;

using static SemiPlot.Tests.Unit.ResultText;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class OpcUaConnectionLoaderTests : IDisposable
{
	private static readonly (string Field, string Value)[] _validFields =
	[
		("host", "127.0.0.1"),
		("port", "4841")
	];

	private readonly TemporaryDirectory _directory = new("semiplot-opc");

	public void Dispose()
	{
		_directory.Dispose();
	}

	[Fact]
	public void TheTrackedShapeLoads()
	{
		WriteFile(Compose(_validFields));

		var result = OpcUaConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Host.Should().Be("127.0.0.1");
		result.Value.Port.Should().Be(4841);
		result.Value.EndpointUrl.Should().Be("opc.tcp://127.0.0.1:4841");
		result.Value.CertificateStoreRoot.Should().Be(Path.Combine(_directory, "pki"));
	}

	[Fact]
	public void AnAbsentSectionYieldsTheOpcSectionsError()
	{
		var absent = Path.Combine(_directory, "opc");

		var result = OpcUaConnectionLoader.Load(absent);

		var error = ErrorOf<ConfigurationSectionError>(result);
		error.Problem.Should().Be(SectionProblem.DirectoryMissing);
		error.Section.Should().Be(ConfigurationSectionName.Opc);
		error.Directory.Should().Be(absent);
	}

	[Theory]
	[InlineData("host")]
	[InlineData("port")]
	public void AnAbsentFieldYieldsTheMissingFieldDiscriminator(string field)
	{
		WriteFile(Compose(_validFields.Where(pair => pair.Field != field)));

		var result = OpcUaConnectionLoader.Load(_directory);

		AssertFileError(result, ConnectionFileProblem.MissingField).Fields.Should().Equal(field);
	}

	// A bare key carries a null, which the nullable DTO reports as absent.
	[Theory]
	[InlineData("host", "\"   \"")]
	[InlineData("host", "")]
	[InlineData("port", "")]
	public void ABlankFieldYieldsTheMissingFieldDiscriminator(string field, string value)
	{
		WriteFile(Compose(Replace(field, value)));

		var result = OpcUaConnectionLoader.Load(_directory);

		AssertFileError(result, ConnectionFileProblem.MissingField).Fields.Should().Equal(field);
	}

	[Fact]
	public void BothAbsentFieldsAreNamedInOneError()
	{
		WriteFile("other: 1\n");

		var result = OpcUaConnectionLoader.Load(_directory);

		AssertFileError(result, ConnectionFileProblem.MissingField).Fields.Should().Equal("host", "port");
	}

	[Theory]
	[InlineData("0")]
	[InlineData("65536")]
	public void APortOutsideItsRangeYieldsTheOutOfRangeDiscriminator(string value)
	{
		WriteFile(Compose(Replace("port", value)));

		var result = OpcUaConnectionLoader.Load(_directory);

		AssertFileError(result, ConnectionFileProblem.OutOfRange).Fields.Should().Equal("port");
	}

	[Theory]
	[InlineData("localhost")]
	[InlineData("WORKING-PC")]
	[InlineData("127.0.0.01")]
	public void AHostThatIsNotAnIPv4AddressYieldsTheHostNotIPv4Discriminator(string host)
	{
		WriteFile(Compose(Replace("host", host)));

		var result = OpcUaConnectionLoader.Load(_directory);

		AssertFileError(result, ConnectionFileProblem.HostNotIPv4).Fields.Should().Equal("host");
	}

	[Theory]
	[InlineData("opc-port")]
	[InlineData("\"\"")]
	public void APortThatIsNotANumberYieldsTheUnparseableDiscriminator(string value)
	{
		WriteFile(Compose(Replace("port", value)));

		var result = OpcUaConnectionLoader.Load(_directory);

		var error = AssertFileError(result, ConnectionFileProblem.Unparseable);
		error.Fields.Should().BeEmpty();
		error.Reasons.OfType<ExceptionalError>().Should().ContainSingle();
	}

	[Fact]
	public void AnUnknownKeyIsIgnored()
	{
		WriteFile(Compose(_validFields.Append(("unknown_key", "1000"))));

		var result = OpcUaConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
	}

	// The client's certificate stores sit under the section, and a section folder is read top-level only.
	[Fact]
	public void AYamlFileUnderThePkiFolderIsNotRead()
	{
		WriteFile(Compose(_validFields));

		var pki = Path.Combine(_directory, "pki");
		Directory.CreateDirectory(pki);
		File.WriteAllText(Path.Combine(pki, "stores.yaml"), "port: 0\nhost: localhost\n");

		var result = OpcUaConnectionLoader.Load(_directory);

		result.IsSuccess.Should().BeTrue(Describe(result));
		result.Value.Port.Should().Be(4841);
	}

	private ConnectionFileError AssertFileError(IResultBase result, ConnectionFileProblem kind)
	{
		var error = ErrorOf<ConnectionFileError>(result);
		error.Section.Should().Be(ConfigurationSectionName.Opc);
		error.Path.Should().Be(_directory);
		error.Kind.Should().Be(kind);

		return error;
	}

	private static IEnumerable<(string Field, string Value)> Replace(string field, string value)
	{
		return _validFields.Select(pair => pair.Field == field ? (field, value) : pair);
	}

	private static string Compose(IEnumerable<(string Field, string Value)> fields)
	{
		return string.Join("\n", fields.Select(pair => $"{pair.Field}: {pair.Value}")) + "\n";
	}

	private void WriteFile(string content)
	{
		File.WriteAllText(Path.Combine(_directory, "opc.yaml"), content);
	}
}
