using System.Globalization;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Export;
using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Export;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveCsvWriterTests
{
	private static readonly DateTime _moment = new(2026, 10, 13, 8, 0, 0, 123, DateTimeKind.Utc);

	private static readonly CsvHeader _englishHeader = new("Pen", "Time", "Value");
	private static readonly CsvHeader _russianHeader = new(
		"\u041F\u0435\u0440\u043E",
		"\u0412\u0440\u0435\u043C\u044F",
		"\u0417\u043D\u0430\u0447\u0435\u043D\u0438\u0435");

	private static CsvDialect Russian => CsvDialect.For(CultureInfo.GetCultureInfo("ru-RU"), _russianHeader);

	private static CsvDialect English => CsvDialect.For(CultureInfo.GetCultureInfo("en-US"), _englishHeader);

	private static Pen PenNamed(string name, string? unit = "bar", int penId = 7)
	{
		return new Pen(penId, name, [], "#FF0000", unit);
	}

	private static string WriteRows(CsvDialect dialect, Pen pen, params double?[] values)
	{
		var rows = values.Select(value => new StoredArchiveRow(pen.PenId, _moment, value)).ToArray();

		return WriteRows(dialect, [pen], rows);
	}

	private static string WriteRows(CsvDialect dialect, IReadOnlyList<Pen> pens, params StoredArchiveRow[] rows)
	{
		var writer = new StringWriter();

		ArchiveCsvWriter.WriteRows(writer, dialect, ArchiveCsvWriter.PenFields(pens, dialect), rows);

		return writer.ToString();
	}

	[Theory]
	[InlineData(
		true,
		"\u041F\u0435\u0440\u043E;\u0412\u0440\u0435\u043C\u044F;\u0417\u043D\u0430\u0447\u0435\u043D\u0438\u0435\r\n")]
	[InlineData(false, "Pen,Time,Value\r\n")]
	public void WriteHeader_WritesThePenTimeAndValueNamesOfTheDialect(bool russian, string expected)
	{
		var writer = new StringWriter();

		ArchiveCsvWriter.WriteHeader(writer, russian ? Russian : English);

		writer.ToString().Should().Be(expected);
	}

	[Fact]
	public void WriteHeader_QuotesAHeaderStringHoldingTheDelimiter()
	{
		var writer = new StringWriter();

		ArchiveCsvWriter.WriteHeader(writer, CsvDialect.For(CultureInfo.GetCultureInfo("en-US"), new CsvHeader("Pen, name", "Time", "Value")));

		writer.ToString().Should().Be("\"Pen, name\",Time,Value\r\n");
	}

	[Fact]
	public void WriteRows_Russian_UsesSemicolonAndDecimalComma()
	{
		var text = WriteRows(Russian, PenNamed("Pressure"), 1.91);

		text.Should().Be("Pressure, bar;2026-10-13 08:00:00,123;1,91\r\n");
	}

	[Fact]
	public void WriteRows_English_QuotesThePenFieldHoldingItsUnitComma()
	{
		var text = WriteRows(English, PenNamed("Pressure"), 1.91);

		text.Should().Be("\"Pressure, bar\",2026-10-13 08:00:00.123,1.91\r\n");
	}

	[Fact]
	public void WriteRows_NegativeValue_UsesAsciiMinusUnderBothDialects()
	{
		WriteRows(Russian, PenNamed("p"), -0.5).Should().EndWith(";-0,5\r\n");
		WriteRows(English, PenNamed("p"), -0.5).Should().EndWith(",-0.5\r\n");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void WriteRows_PenWithoutAUnit_WritesItsNameAlone(string? unit)
	{
		var text = WriteRows(English, PenNamed("p", unit), (double?)null);

		text.Should().Be("p,2026-10-13 08:00:00.123,\r\n");
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void WriteRows_NonFiniteValues_AreWrittenInvariantly(bool russian)
	{
		var dialect = russian ? Russian : English;

		var text = WriteRows(
			dialect, PenNamed("p", unit: null), double.NaN, double.PositiveInfinity, double.NegativeInfinity);

		var values = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
			.Select(line => line.Split(dialect.Delimiter)[2]);

		values.Should().Equal("NaN", "Infinity", "-Infinity");
	}

	[Fact]
	public void WriteRows_NameWithDelimiterQuoteAndLineBreak_IsQuoted()
	{
		var text = WriteRows(English, PenNamed("a,\"b\"\r\nc", unit: null), 1);

		text.Should().StartWith("\"a,\"\"b\"\"\r\nc\",");
	}

	[Fact]
	public void WriteRows_NameWithOtherDialectDelimiter_StaysUnquoted()
	{
		var text = WriteRows(English, PenNamed("a;b", unit: null), 1);

		text.Should().StartWith("a;b,");
	}

	[Fact]
	public void WriteRows_Russian_QuotesAPenFieldHoldingASemicolon_AndLeavesAComma()
	{
		var text = WriteRows(Russian, PenNamed("a;b,c", unit: "x;y"), 1);

		text.Should().StartWith("\"a;b,c, x;y\";");
	}

	[Fact]
	public void WriteRows_Russian_LeavesTheCommaOfAUnitUnquoted()
	{
		var text = WriteRows(Russian, PenNamed("Damper 01", unit: "%"), 1);

		text.Should().StartWith("Damper 01, %;");
	}

	[Fact]
	public void WriteRows_RowsOfTwoPens_AreWrittenInTheOrderGiven()
	{
		var pens = new[] { PenNamed("a", unit: null, penId: 1), PenNamed("b", unit: null, penId: 2) };

		var text = WriteRows(
			English,
			pens,
			new StoredArchiveRow(2, _moment, 1),
			new StoredArchiveRow(1, _moment, 2),
			new StoredArchiveRow(2, _moment.AddSeconds(1), 3));

		text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Select(line => line[0]).Should().Equal('b', 'a', 'b');
	}

	[Fact]
	public void PenFields_TwoTickedPensNamedDamper01_GetTheirIds()
	{
		var pens = new[]
		{
			PenNamed("Damper 01", unit: "%", penId: 17),
			PenNamed("Damper 01", unit: null, penId: 18),
			PenNamed("Argon flow", unit: "sccm", penId: 19)
		};

		var fields = ArchiveCsvWriter.PenFields(pens, Russian);

		fields[17].Should().Be("Damper 01, % (17)");
		fields[18].Should().Be("Damper 01 (18)");
		fields[19].Should().Be("Argon flow, sccm");
	}

	[Fact]
	public void PenFields_English_QuotesTheFieldWithTheIdInsideTheQuotes()
	{
		var pens = new[] { PenNamed("Damper 01", unit: "%", penId: 17), PenNamed("Damper 01", unit: "%", penId: 18) };

		var fields = ArchiveCsvWriter.PenFields(pens, English);

		fields[17].Should().Be("\"Damper 01, % (17)\"");
	}

	[Fact]
	public void PenFields_NamesDifferingOnlyInCase_DoNotCollide()
	{
		var pens = new[] { PenNamed("Damper 01", unit: null, penId: 1), PenNamed("damper 01", unit: null, penId: 2) };

		var fields = ArchiveCsvWriter.PenFields(pens, English);

		fields[1].Should().Be("Damper 01");
		fields[2].Should().Be("damper 01");
	}

	[Fact]
	public void PenFields_DifferentNamesComposingOneField_GetTheirIds()
	{
		var pens = new[]
		{
			PenNamed("Damper 01, %", unit: null, penId: 1),
			PenNamed("Damper 01", unit: "%", penId: 2)
		};

		var fields = ArchiveCsvWriter.PenFields(pens, Russian);

		fields[1].Should().Be("Damper 01, % (1)");
		fields[2].Should().Be("Damper 01, % (2)");
	}

	[Fact]
	public void PenFields_ANamesakeOutsideTheTickedSet_AddsNoId()
	{
		var ticked = new[] { PenNamed("Damper 01", unit: null, penId: 1) };

		var fields = ArchiveCsvWriter.PenFields(ticked, English);

		fields[1].Should().Be("Damper 01");
	}

	[Theory]
	[InlineData(DateTimeKind.Utc)]
	[InlineData(DateTimeKind.Local)]
	[InlineData(DateTimeKind.Unspecified)]
	public void WriteRows_Timestamp_IsWrittenVerbatimWhateverTheKind(DateTimeKind kind)
	{
		var time = new DateTime(2026, 10, 25, 2, 30, 0, 7, kind);

		var text = WriteRows(English, [PenNamed("p", unit: null)], new StoredArchiveRow(7, time, 1));

		text.Should().Contain(",2026-10-25 02:30:00.007,");
	}
}
