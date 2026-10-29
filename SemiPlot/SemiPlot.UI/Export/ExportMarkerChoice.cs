using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Export;

/// <summary>
/// One item of a range edge's marker box; <c>WallClock</c> is the marker's archive wall-clock moment.
/// </summary>
public sealed record ExportMarkerChoice(StoredMarker? Marker, DateTime WallClock, string Label)
{
	private static readonly MarkerKind[] _startKinds = [MarkerKind.User, MarkerKind.RecipeStart];
	private static readonly MarkerKind[] _endKinds =
		[MarkerKind.User, MarkerKind.RecipeEnd, MarkerKind.RecipeInterrupted];

	/// <summary>The box's first item, which leaves its edge to the typed time.</summary>
	public bool IsNoMarker => Marker is null;

	/// <summary>The open list's text; the closed box shows <see cref="Label"/> beside the time box.</summary>
	public string Text => IsNoMarker ? Label : $"{WallClockRangeForm.FormatTime(WallClock)} {Label}";

	internal DateTime StartValue => WallClockRangeForm.FloorToSecond(WallClock);

	/// <summary>The end a choice writes: past the marker's second, so the export keeps every row of it.</summary>
	internal DateTime EndValue => StartValue.AddSeconds(1);

	/// <summary>A marker of its day's last second, whose end falls on the next date, where no list offers it.</summary>
	internal bool EndLeavesItsDate => EndValue.Date != WallClock.Date;

	internal static ExportMarkerChoice NoMarker()
	{
		return new ExportMarkerChoice(null, default, Resources.ExportNoMarker);
	}

	/// <summary>The start-kind markers on the date the start calendar holds.</summary>
	internal static IReadOnlyList<ExportMarkerChoice> ForStart(
		IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone, DateTime? date)
	{
		return [.. OnDate(markers, timeZone, date, _startKinds)];
	}

	internal static IReadOnlyList<ExportMarkerChoice> ForEnd(
		IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone, DateTime? date, DateTime? startBound)
	{
		return [.. OnDate(markers, timeZone, date, _endKinds)
			.Where(choice => startBound is not { } start || choice.StartValue > start)];
	}

	private static IEnumerable<ExportMarkerChoice> OnDate(
		IReadOnlyList<StoredMarker> markers, TimeZoneInfo timeZone, DateTime? date, MarkerKind[] kinds)
	{
		if (date is not { } day)
		{
			return [];
		}

		return markers
			.Where(marker => kinds.Contains(marker.Kind))
			.OrderBy(marker => marker.TimeUtc)
			.ThenBy(marker => marker.Id)
			.Select(marker => Choice(marker, WallClockRangeForm.ToWallClock(marker.TimeUtc, timeZone)))
			.Where(choice => choice.WallClock.Date == day.Date);
	}

	private static ExportMarkerChoice Choice(StoredMarker marker, DateTime wallClock)
	{
		var label = string.IsNullOrWhiteSpace(marker.Label) ? Resources.ExportMarkerUnnamed : marker.Label;

		return new ExportMarkerChoice(marker, wallClock, label);
	}
}
