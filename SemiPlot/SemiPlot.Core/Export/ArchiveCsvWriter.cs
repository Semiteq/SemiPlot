using System.Globalization;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Export;

public static class ArchiveCsvWriter
{
	private const string LineEnd = "\r\n";

	public static void WriteHeader(TextWriter writer, CsvDialect dialect)
	{
		var header = dialect.Header;

		writer.Write(string.Join(
			dialect.Delimiter,
			Quote(header.Pen, dialect.Delimiter),
			Quote(header.Time, dialect.Delimiter),
			Quote(header.Value, dialect.Delimiter)));
		writer.Write(LineEnd);
	}

	/// <summary>Pen id to its quoted file field; pens that share a name or a whole field carry their id.</summary>
	public static IReadOnlyDictionary<int, string> PenFields(IReadOnlyList<Pen> pens, CsvDialect dialect)
	{
		var sharedNames = PenNaming.SharedKeys(pens, pen => pen.Name);
		var sharedFields = PenNaming.SharedKeys(pens, ComposeField);
		var fields = new Dictionary<int, string>(pens.Count);

		foreach (var pen in pens)
		{
			var field = ComposeField(pen);

			if (sharedNames.Contains(pen.Name) || sharedFields.Contains(field))
			{
				field = PenNaming.WithId(field, pen);
			}

			fields[pen.PenId] = Quote(field, dialect.Delimiter);
		}

		return fields;
	}

	public static void WriteRows(
		TextWriter writer,
		CsvDialect dialect,
		IReadOnlyDictionary<int, string> penFields,
		IReadOnlyList<StoredArchiveRow> rows)
	{
		foreach (var row in rows)
		{
			var timestamp = row.Time.ToString(dialect.TimestampFormat, CultureInfo.InvariantCulture);
			var value = FormatValue(row.Value, dialect);

			writer.Write(string.Join(dialect.Delimiter, penFields[row.PenId], timestamp, value));
			writer.Write(LineEnd);
		}
	}

	private static string ComposeField(Pen pen)
	{
		return string.IsNullOrEmpty(pen.Unit) ? pen.Name : $"{pen.Name}, {pen.Unit}";
	}

	private static string FormatValue(double? value, CsvDialect dialect)
	{
		if (value is not { } number)
		{
			return string.Empty;
		}

		if (double.IsNaN(number))
		{
			return "NaN";
		}

		if (double.IsInfinity(number))
		{
			return number > 0 ? "Infinity" : "-Infinity";
		}

		return number.ToString("R", dialect.NumberCulture);
	}

	private static string Quote(string field, char delimiter)
	{
		var needsQuotes = field.Contains(delimiter) || field.AsSpan().IndexOfAny('"', '\r', '\n') >= 0;

		return needsQuotes ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
	}
}
