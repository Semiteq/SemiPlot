using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>The catalogue as stored, for the editor: no value is normalised the way the chart's read does.</summary>
public sealed record PenCatalogue(IReadOnlyList<StoredPen> Pens, IReadOnlyList<StoredGroup> Groups);

/// <summary>
/// The one catalogue read: the stored catalogue the editor edits and the chart's pens, both projections of
/// the same rows, so no caller reads the catalogue twice.
/// </summary>
public sealed record CatalogueRead(PenCatalogue Stored, IReadOnlyList<Pen> Pens)
{
	/// <summary>
	/// The same pens, stored pens and stored groups by id, in any order; a stored change the normalised pens
	/// hide, or a group's own column, does not match.
	/// </summary>
	public bool Matches(CatalogueRead other)
	{
		return PenListDelta.Between(Pens, other.Pens).IsEmpty
			&& StoredPensMatch(Stored.Pens, other.Stored.Pens)
			&& StoredGroupsMatch(Stored.Groups, other.Stored.Groups);
	}

	private static bool StoredPensMatch(IReadOnlyList<StoredPen> pens, IReadOnlyList<StoredPen> otherPens)
	{
		var otherById = otherPens.ToDictionary(pen => pen.Id);

		return pens.Count == otherById.Count
			&& pens.All(pen => otherById.TryGetValue(pen.Id, out var otherPen) && otherPen.Equals(pen));
	}

	private static bool StoredGroupsMatch(
		IReadOnlyList<StoredGroup> groups,
		IReadOnlyList<StoredGroup> otherGroups)
	{
		var otherById = otherGroups.ToDictionary(group => group.Id);

		return groups.Count == otherById.Count
			&& groups.All(group =>
				otherById.TryGetValue(group.Id, out var otherGroup) && GroupsEqual(group, otherGroup));
	}

	/// <summary>
	/// Field by field: <see cref="StoredGroup.MemberPenIds"/> is a list, so record equality would compare it by
	/// reference.
	/// </summary>
	private static bool GroupsEqual(StoredGroup group, StoredGroup other)
	{
		return group.Name == other.Name
			&& group.FoldedOnStart == other.FoldedOnStart
			&& group.MemberPenIds.SequenceEqual(other.MemberPenIds);
	}
}

/// <summary>One <c>semiplot_tags</c> row as stored; a null field is a stored <c>NULL</c>.</summary>
public sealed record StoredPen(
	int Id,
	string Name,
	string? Unit,
	string? Format,
	string? Color,
	PenLineStyle LineStyle,
	bool EnabledOnStart,
	double? ScaleMinOnStart,
	double? ScaleMaxOnStart,
	bool LogScaleOnStart);

/// <summary>One <c>semiplot_groups</c> row and the ids of its member pens, in ascending order.</summary>
public sealed record StoredGroup(int Id, string Name, IReadOnlyList<int> MemberPenIds, bool FoldedOnStart = false);
