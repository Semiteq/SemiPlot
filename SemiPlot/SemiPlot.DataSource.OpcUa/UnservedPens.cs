using Opc.Ua;

using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>One monitored item's creation outcome, or its status read after the session was recreated.</summary>
internal readonly record struct ItemStatus(int PenId, bool Created, StatusCode Status);

/// <summary>
/// One subscription's pens the source cannot serve, unknown or not numeric: one
/// <see cref="LiveSourceFault.PensUnserved"/> once every item has answered or the settle time has passed, again
/// whenever the set grows; a pen served again leaves it. Only the source's loop calls it.
/// </summary>
internal sealed class UnservedPens(IReadOnlyList<int> penIds, string endpoint, ReportedUnservedSets reported)
{
	private readonly HashSet<int> _waiting = [.. penIds];
	private readonly SortedSet<int> _unknown = [];
	private readonly SortedSet<int> _notNumeric = [];
	private bool _isSettled;

	/// <summary>
	/// Only <c>BadNodeIdUnknown</c> makes a pen unknown; an item that failed for another reason never answers and is
	/// logged by its subscription, and a created item leaves the unknown set.
	/// </summary>
	public LiveSourceError? OnItemStatuses(IEnumerable<ItemStatus> statuses)
	{
		var grew = false;

		foreach (var status in statuses)
		{
			if (status.Status == StatusCodes.BadNodeIdUnknown)
			{
				_waiting.Remove(status.PenId);
				grew |= _unknown.Add(status.PenId);
			}
			else if (StatusCode.IsBad(status.Status))
			{
				_waiting.Remove(status.PenId);
			}
			else if (status.Created)
			{
				_unknown.Remove(status.PenId);
			}
		}

		return Report(grew);
	}

	/// <summary>A pen's number: it answered and is numeric again.</summary>
	public LiveSourceError? OnValue(int penId)
	{
		_notNumeric.Remove(penId);

		return OnAnswered(penId);
	}

	/// <summary>A pen's notification the line can place without a number: a break, or a value with no stamp.</summary>
	public LiveSourceError? OnAnswered(int penId)
	{
		_waiting.Remove(penId);

		return Report(grew: false);
	}

	public LiveSourceError? OnNotNumeric(int penId)
	{
		_waiting.Remove(penId);
		var grew = _notNumeric.Add(penId);

		return Report(grew);
	}

	public LiveSourceError? OnSettleElapsed()
	{
		_waiting.Clear();

		return Report(grew: false);
	}

	private LiveSourceError? Report(bool grew)
	{
		if (_isSettled && !grew)
		{
			return null;
		}

		if (!_isSettled && _waiting.Count > 0)
		{
			return null;
		}

		_isSettled = true;

		if (_unknown.Count == 0 && _notNumeric.Count == 0)
		{
			return null;
		}

		int[] unknown = [.. _unknown];
		int[] notNumeric = [.. _notNumeric];

		return reported.TryAdd(unknown, notNumeric)
			? new LiveSourceError(LiveSourceFault.PensUnserved, endpoint, unknown, notNumeric)
			: null;
	}
}
