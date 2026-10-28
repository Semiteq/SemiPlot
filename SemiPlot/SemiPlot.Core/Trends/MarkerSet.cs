using SemiPlot.Core.Data;

namespace SemiPlot.Core.Trends;

/// <summary>
/// The markers a window holds, sorted by time then id, and the ones it shows; a held marker keeps its local state.
/// </summary>
public sealed class MarkerSet
{
	private static readonly Comparer<StoredMarker> _byTimeThenId = Comparer<StoredMarker>.Create(CompareByTimeThenId);

	private readonly Dictionary<long, int> _holdCounts = [];

	private StoredMarker[] _all = [];
	private StoredMarker[] _shownMarkers = [];

	/// <summary>Every marker, shown or hidden; a snapshot like <see cref="ShownMarkers"/>.</summary>
	public IReadOnlyList<StoredMarker> All => _all;

	/// <summary>
	/// The shown markers as a snapshot: every change replaces the array, so a reader never sees it mutate.
	/// </summary>
	public IReadOnlyList<StoredMarker> ShownMarkers => _shownMarkers;

	public bool ShowsRecipeMarkers { get; private set; } = true;

	public void ToggleRecipeMarkers()
	{
		ShowsRecipeMarkers = !ShowsRecipeMarkers;
		RebuildShown();
	}

	public bool Apply(IReadOnlyList<StoredMarker> read)
	{
		var heldLocally = new Dictionary<long, StoredMarker>();
		foreach (var marker in _all)
		{
			if (_holdCounts.ContainsKey(marker.Id))
			{
				heldLocally[marker.Id] = marker;
			}
		}

		var next = new List<StoredMarker>(read.Count + heldLocally.Count);
		foreach (var marker in read)
		{
			if (heldLocally.Remove(marker.Id, out var local))
			{
				next.Add(local);
			}
			else
			{
				next.Add(marker);
			}
		}

		next.AddRange(heldLocally.Values);
		next.Sort(_byTimeThenId);

		if (next.SequenceEqual(_all))
		{
			return false;
		}

		_all = [.. next];
		RebuildShown();

		return true;
	}

	public void Hold(long id)
	{
		_holdCounts[id] = _holdCounts.GetValueOrDefault(id) + 1;
	}

	/// <summary>Returns whether this release brought the marker's hold count to zero.</summary>
	public bool Release(long id)
	{
		if (!_holdCounts.TryGetValue(id, out var count))
		{
			return false;
		}

		if (count > 1)
		{
			_holdCounts[id] = count - 1;

			return false;
		}

		_holdCounts.Remove(id);

		return true;
	}

	/// <summary>An upsert by id, one copy pass per list the marker is in; it runs on every move of a drag.</summary>
	public void Place(StoredMarker marker)
	{
		_all = Upsert(_all, marker);
		if (ShowsRecipeMarkers)
		{
			_shownMarkers = _all;
		}
		else if (IsShownWhileRecipesHidden(marker))
		{
			_shownMarkers = Upsert(_shownMarkers, marker);
		}
	}

	public void Remove(long id)
	{
		_all = Array.FindAll(_all, marker => marker.Id != id);
		RebuildShown();
	}

	/// <summary>The marker closest to <paramref name="time"/> within the tolerance; the lower id wins a tie.</summary>
	public StoredMarker? NearestWithin(DateTime time, TimeSpan tolerance)
	{
		var right = FirstAtOrAfter(time);
		var left = right - 1;
		while (left > 0 && _shownMarkers[left - 1].TimeUtc == _shownMarkers[left].TimeUtc)
		{
			left--;
		}

		StoredMarker? nearest = null;
		var nearestDistance = TimeSpan.MaxValue;
		foreach (var index in (ReadOnlySpan<int>)[left, right])
		{
			if (index < 0 || index >= _shownMarkers.Length)
			{
				continue;
			}

			var candidate = _shownMarkers[index];
			var distance = (candidate.TimeUtc - time).Duration();
			if (distance > tolerance)
			{
				continue;
			}

			if (nearest is null || distance < nearestDistance
				|| (distance == nearestDistance && candidate.Id < nearest.Id))
			{
				nearest = candidate;
				nearestDistance = distance;
			}
		}

		return nearest;
	}

	private static int CompareByTimeThenId(StoredMarker first, StoredMarker second)
	{
		var byTime = first.TimeUtc.CompareTo(second.TimeUtc);

		return byTime != 0 ? byTime : first.Id.CompareTo(second.Id);
	}

	private static bool IsShownWhileRecipesHidden(StoredMarker marker)
	{
		return marker.Kind == MarkerKind.User;
	}

	private static StoredMarker[] Upsert(StoredMarker[] sorted, StoredMarker marker)
	{
		var isReplacing = Array.Exists(sorted, existing => existing.Id == marker.Id);
		var next = new StoredMarker[isReplacing ? sorted.Length : sorted.Length + 1];
		var written = 0;
		var isPlaced = false;
		foreach (var existing in sorted)
		{
			if (existing.Id == marker.Id)
			{
				continue;
			}

			if (!isPlaced && _byTimeThenId.Compare(marker, existing) < 0)
			{
				next[written++] = marker;
				isPlaced = true;
			}

			next[written++] = existing;
		}

		if (!isPlaced)
		{
			next[written] = marker;
		}

		return next;
	}

	private void RebuildShown()
	{
		_shownMarkers = ShowsRecipeMarkers ? _all : Array.FindAll(_all, IsShownWhileRecipesHidden);
	}

	private int FirstAtOrAfter(DateTime time)
	{
		var low = 0;
		var high = _shownMarkers.Length;
		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (_shownMarkers[middle].TimeUtc < time)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}
}
