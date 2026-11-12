using Avalonia.Input;

using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Navigation;

/// <summary>
/// The navigation gestures: each Go item shows one, the window runs the item by it, tooltips name it.
/// </summary>
public static class WindowGestures
{
	public static readonly KeyGesture PreviousPage = new(Key.Left, KeyModifiers.Alt);

	public static readonly KeyGesture NextPage = new(Key.Right, KeyModifiers.Alt);

	public static readonly KeyGesture PreviousRun = new(Key.Left, KeyModifiers.Control);

	public static readonly KeyGesture NextRun = new(Key.Right, KeyModifiers.Control);

	public static readonly KeyGesture LatestRun = new(Key.End, KeyModifiers.Control);

	public static readonly KeyGesture RecipeRunList = new(Key.R, KeyModifiers.Control);

	public static readonly KeyGesture PreviousMarker = new(Key.Left, KeyModifiers.Control | KeyModifiers.Shift);

	public static readonly KeyGesture NextMarker = new(Key.Right, KeyModifiers.Control | KeyModifiers.Shift);

	public static readonly KeyGesture MarkerList = new(Key.M, KeyModifiers.Control);

	public static string PreviousRunTip => Resources.FormatNavigationPreviousRunTip(TextOf(PreviousRun));

	public static string NextRunTip => Resources.FormatNavigationNextRunTip(TextOf(NextRun));

	public static string LatestRunTip => Resources.FormatNavigationLatestRunTip(TextOf(LatestRun));

	public static string RecipeRunListTip => Resources.FormatNavigationRecipeRunsTip(TextOf(RecipeRunList));

	public static string MarkerListTip => Resources.FormatNavigationMarkersTip(TextOf(MarkerList));

	public static string PreviousDayTip => Resources.FormatMinimapPreviousDayTip(TextOf(PreviousPage));

	public static string NextDayTip => Resources.FormatMinimapNextDayTip(TextOf(NextPage));

	public static string PreviousWeekTip => Resources.FormatMinimapPreviousWeekTip(TextOf(PreviousPage));

	public static string NextWeekTip => Resources.FormatMinimapNextWeekTip(TextOf(NextPage));

	public static string PreviousMonthTip => Resources.FormatMinimapPreviousMonthTip(TextOf(PreviousPage));

	public static string NextMonthTip => Resources.FormatMinimapNextMonthTip(TextOf(NextPage));

	/// <summary>The gesture's text as a menu item prints it beside its caption.</summary>
	private static string TextOf(KeyGesture gesture)
	{
		return gesture.ToString("p", null);
	}
}
