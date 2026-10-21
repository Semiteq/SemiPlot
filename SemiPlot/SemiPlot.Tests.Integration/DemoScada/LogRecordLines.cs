using Microsoft.Extensions.Logging.Testing;

namespace SemiPlot.Tests.Integration.DemoScada;

/// <summary>Collected log records as text for an assertion's reason, one record per line.</summary>
internal static class LogRecordLines
{
	public static string Of(IEnumerable<FakeLogRecord> records)
	{
		return string.Join(
			Environment.NewLine,
			records.Select(record => $"{record.Level} {record.Category}: {record.Message} {record.Exception}"));
	}
}
