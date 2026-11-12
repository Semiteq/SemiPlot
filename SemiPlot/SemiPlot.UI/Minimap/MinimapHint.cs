using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Minimap;

/// <summary>The detail track's hint for a state its band cannot show; null while the band speaks for itself.</summary>
internal static class MinimapHint
{
	public static string? For(
		bool isArchiveEmpty,
		bool hasExtent,
		MinimapPage page,
		IReadOnlyList<ArchiveDaySpan> spans,
		bool hasBandReadFailed)
	{
		if (!hasExtent)
		{
			return isArchiveEmpty ? Resources.MinimapNoRowsInArchive : null;
		}

		// Without spans the day list is unknown, so no page is declared empty.
		if (spans.Count > 0 && !spans.Any(span => page.Holds(span.Day)))
		{
			return Resources.FormatMinimapNoRowsOn(MinimapLabels.Page(page));
		}

		return hasBandReadFailed ? Resources.MinimapBandNotRead : null;
	}
}
