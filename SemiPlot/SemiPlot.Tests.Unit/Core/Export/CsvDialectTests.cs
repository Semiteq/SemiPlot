using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Core.Export;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Export;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class CsvDialectTests
{
	private static readonly CsvHeader _header = new("Pen", "Time", "Value");

	private static CultureInfo Regional(string name, string? listSeparator = null, string? decimalSign = null)
	{
		var culture = (CultureInfo)CultureInfo.GetCultureInfo(name).Clone();

		if (listSeparator is not null)
		{
			culture.TextInfo.ListSeparator = listSeparator;
		}

		if (decimalSign is not null)
		{
			culture.NumberFormat.NumberDecimalSeparator = decimalSign;
		}

		return culture;
	}

	[Theory]
	[InlineData("ru-RU", ';', ",", "yyyy-MM-dd HH:mm:ss,fff")]
	[InlineData("de-DE", ';', ",", "yyyy-MM-dd HH:mm:ss,fff")]
	[InlineData("en-US", ',', ".", "yyyy-MM-dd HH:mm:ss.fff")]
	public void For_AKnownCulture_TakesItsListSeparatorAndDecimalSign(
		string name, char delimiter, string decimalSign, string timestampFormat)
	{
		var dialect = CsvDialect.For(Regional(name), _header);

		dialect.Delimiter.Should().Be(delimiter);
		dialect.NumberCulture.NumberFormat.NumberDecimalSeparator.Should().Be(decimalSign);
		dialect.TimestampFormat.Should().Be(timestampFormat);
		dialect.Header.Should().Be(_header);
	}

	[Theory]
	[InlineData(",", ",", ';', ",")]
	[InlineData(".", ".", ',', ".")]
	[InlineData(";;", ".", ',', ".")]
	[InlineData("", ",", ';', ",")]
	[InlineData("\"", ".", ',', ".")]
	[InlineData("\r", ",", ';', ",")]
	[InlineData("\n", ".", ',', ".")]
	[InlineData(";", "'", ';', ".")]
	[InlineData(".", "'", ',', ".")]
	[InlineData(";", ",,", ';', ".")]
	[InlineData(" ", ".", ',', ".")]
	[InlineData("-", ",", ';', ",")]
	[InlineData(":", ".", ',', ".")]
	[InlineData("5", ",", ';', ",")]
	[InlineData("a", ".", ',', ".")]
	[InlineData("E", ",", ';', ",")]
	public void For_AnUnusableSeparatorPair_FallsBack(
		string listSeparator, string decimalSign, char delimiter, string expectedDecimalSign)
	{
		var dialect = CsvDialect.For(Regional("en-US", listSeparator, decimalSign), _header);

		dialect.Delimiter.Should().Be(delimiter);
		dialect.NumberCulture.NumberFormat.NumberDecimalSeparator.Should().Be(expectedDecimalSign);
		dialect.TimestampFormat.Should().Be($"yyyy-MM-dd HH:mm:ss{expectedDecimalSign}fff");
	}

	[Theory]
	[InlineData("|")]
	[InlineData("\t")]
	public void For_ANonStandardButUsableListSeparator_IsKept(string listSeparator)
	{
		var dialect = CsvDialect.For(Regional("en-US", listSeparator), _header);

		dialect.Delimiter.Should().Be(listSeparator[0]);
	}

	[Fact]
	public void For_NumberCulture_ChangesOnlyTheDecimalSeparatorOfTheInvariantCulture()
	{
		var dialect = CsvDialect.For(Regional("ru-RU"), _header);

		var format = dialect.NumberCulture.NumberFormat;

		format.NegativeSign.Should().Be("-");
		format.NaNSymbol.Should().Be(CultureInfo.InvariantCulture.NumberFormat.NaNSymbol);
		format.PositiveInfinitySymbol.Should().Be(CultureInfo.InvariantCulture.NumberFormat.PositiveInfinitySymbol);
		(-1234.5).ToString("R", dialect.NumberCulture).Should().Be("-1234,5");
	}
}
