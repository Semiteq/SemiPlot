using System.Globalization;

namespace SemiPlot.Core.Export;

public sealed record CsvHeader(string Pen, string Time, string Value);

public sealed record CsvDialect(char Delimiter, CultureInfo NumberCulture, string TimestampFormat, CsvHeader Header)
{
	private const char CommaSign = ',';
	private const char DotSign = '.';

	/// <summary>The dialect of the regional settings: its list separator and its decimal sign, with fallbacks.</summary>
	public static CsvDialect For(CultureInfo regional, CsvHeader header)
	{
		var decimalSign = regional.NumberFormat.NumberDecimalSeparator == "," ? CommaSign : DotSign;
		var delimiter = DelimiterFor(regional.TextInfo.ListSeparator, decimalSign);

		return new(delimiter, NumberCultureWith(decimalSign), $"yyyy-MM-dd HH:mm:ss{decimalSign}fff", header);
	}

	private static char DelimiterFor(string listSeparator, char decimalSign)
	{
		if (listSeparator.Length == 1
			&& listSeparator[0] != decimalSign
			&& listSeparator[0] is ';' or ',' or '\t' or '|')
		{
			return listSeparator[0];
		}

		return decimalSign == CommaSign ? ';' : CommaSign;
	}

	private static CultureInfo NumberCultureWith(char decimalSign)
	{
		var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

		culture.NumberFormat.NumberDecimalSeparator = decimalSign.ToString();

		return culture;
	}
}
