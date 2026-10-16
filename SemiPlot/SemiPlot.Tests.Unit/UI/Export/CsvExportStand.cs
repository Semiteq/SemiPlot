using System.Globalization;
using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Export;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Export;
using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.Export;

internal static class CsvExportStand
{
	public static readonly TimeZoneInfo PlusThree =
		TimeZoneInfo.CreateCustomTimeZone("PlusThree", TimeSpan.FromHours(3), "PlusThree", "PlusThree");

	public static readonly DateTime StartUtc = new(2026, 10, 13, 5, 0, 0, DateTimeKind.Utc);

	public static readonly DateTime FirstSampleUtc = new(2026, 10, 8, 5, 0, 0, DateTimeKind.Utc);

	public static readonly Pen Pressure = new(7, "Chamber pressure", [], "#1F77B4", "Pa");
	public static readonly Pen Argon = new(3, "Argon flow", [], "#FF7F0E", "sccm");
	public static readonly Pen Power = new(12, "Bias power", [], "#2CA02C");

	public static CsvExportViewModel Build(
		FakeArchiveRowReader reader,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		IReadOnlyList<ExportPenChoice> pens,
		DateTime? newestSampleUtc = null,
		Func<DateOnly, DateOnly, CancellationToken, Task<Result<IReadOnlyList<DateOnly>>>>? readArchiveDays = null)
	{
		return new CsvExportViewModel(
			reader,
			readArchiveDays ?? ((_, _, _) => Task.FromResult(Result.Ok<IReadOnlyList<DateOnly>>([]))),
			CsvDialect.For(CultureInfo.InvariantCulture, new CsvHeader("Pen", "Time", "Value")),
			messagePanel,
			NullLogger<CsvExportViewModel>.Instance,
			uiScheduler,
			PlusThree,
			StartUtc,
			StartUtc.AddHours(1),
			FirstSampleUtc,
			newestSampleUtc,
			pens);
	}
}
