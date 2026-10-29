using System.Globalization;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>
/// The markers one chart shows: every read reconciled into one <see cref="MarkerSet"/>, and the gestures that write
/// through the channel. A write's own result updates the set at once; the channel reports every failure.
/// </summary>
public sealed class ChartMarkers : IDisposable
{
	private const string NumberPrefix = "#";

	private readonly IMarkerChannel _markerChannel;
	private readonly MarkerSet _markerSet = new();
	private readonly Subject<Unit> _changed = new();
	private readonly CompositeDisposable _disposables = [];

	private MarkerDrag? _drag;

	public ChartMarkers(IMarkerChannel markerChannel, TrendChartViewModel chart)
	{
		_markerChannel = markerChannel;

		_disposables.Add(_markerChannel.Reads.Subscribe(ApplyRead, chart.ReportFailure));
	}

	/// <summary>
	/// Every marker the window holds, shown or hidden, in time order, then by id; one with a write in flight as the
	/// window shows it. A new list per change.
	/// </summary>
	public IReadOnlyList<StoredMarker> All => _markerSet.All;

	/// <summary>The shown markers in time order, then by id; a new list per change.</summary>
	public IReadOnlyList<StoredMarker> ShownMarkers => _markerSet.ShownMarkers;

	/// <summary>The window's own flag, not stored; a new window starts with recipe markers shown.</summary>
	public bool ShowsRecipeMarkers => _markerSet.ShowsRecipeMarkers;

	/// <summary>
	/// One signal per change of <see cref="ShownMarkers"/>, raised on the UI thread; completes on dispose.
	/// </summary>
	public IObservable<Unit> Changed => _changed.AsObservable();

	public StoredMarker? NearestWithin(DateTime timeUtc, TimeSpan tolerance)
	{
		return _markerSet.NearestWithin(timeUtc, tolerance);
	}

	public void ToggleRecipeMarkers()
	{
		_markerSet.ToggleRecipeMarkers();
		RaiseChanged();
	}

	/// <summary>
	/// Stores a new marker labelled with the next number; it draws only once its id lands, and a failed add places
	/// nothing.
	/// </summary>
	public async Task AddAtAsync(DateTime timeUtc)
	{
		var label = NumberPrefix + NextNumber().ToString(CultureInfo.InvariantCulture);
		var added = await _markerChannel.AddAsync(timeUtc, label);
		if (added.IsFailed)
		{
			return;
		}

		var placed = new StoredMarker(added.Value, timeUtc, label);
		if (!_markerSet.All.Contains(placed))
		{
			_markerSet.Place(placed);
			RaiseChanged();
		}
	}

	/// <summary>Holds the marker against reads until the move it starts has landed.</summary>
	public void BeginMove(StoredMarker marker)
	{
		_markerSet.Hold(marker.Id);
		_drag = new MarkerDrag(marker, marker.TimeUtc);
	}

	public void Move(DateTime timeUtc)
	{
		if (_drag is not { } drag || drag.ReachedUtc == timeUtc)
		{
			return;
		}

		_drag = drag with { ReachedUtc = timeUtc };
		ReplaceHeld(drag.AtStart.Id, current => current with { TimeUtc = timeUtc });
	}

	/// <summary>
	/// Stores the moment of the last <see cref="Move"/>; a failure restores the moment the drag began at.
	/// </summary>
	public async Task EndMoveAsync()
	{
		if (_drag is not { } drag)
		{
			return;
		}

		_drag = null;

		await WriteHeldAsync(
			drag.AtStart,
			() => _markerChannel.MoveAsync(drag.AtStart, drag.ReachedUtc),
			current => current with { TimeUtc = drag.AtStart.TimeUtc });
	}

	/// <summary>Shows the new label at once; a failure restores the label the marker had.</summary>
	public async Task RenameAsync(StoredMarker marker, string? typedLabel)
	{
		var label = LabelOf(typedLabel);
		_markerSet.Hold(marker.Id);
		ReplaceHeld(marker.Id, current => current with { Label = label });

		await WriteHeldAsync(
			marker,
			() => _markerChannel.RenameAsync(marker, label),
			current => current with { Label = marker.Label });
	}

	/// <summary>
	/// Stores the opposite of the lock <paramref name="seen"/> carried and shows it at once; a failure restores the
	/// lock the set held.
	/// </summary>
	public async Task ToggleLockAsync(StoredMarker seen)
	{
		var isLocked = !seen.IsLocked;
		var marker = FindMarker(seen.Id) ?? seen;
		_markerSet.Hold(marker.Id);
		ReplaceHeld(marker.Id, current => current with { IsLocked = isLocked });

		await WriteHeldAsync(
			marker,
			() => _markerChannel.SetLockedAsync(marker, isLocked),
			current => current with { IsLocked = marker.IsLocked });
	}

	/// <summary>Removes the marker once the delete lands; a failed delete keeps it.</summary>
	public async Task DeleteAsync(StoredMarker marker)
	{
		_markerSet.Hold(marker.Id);

		Result deleted;
		try
		{
			deleted = await _markerChannel.DeleteAsync(marker);
		}
		finally
		{
			_markerSet.Release(marker.Id);
		}

		if (deleted.IsSuccess)
		{
			RemoveMarker(marker.Id);
		}
	}

	public void Dispose()
	{
		_disposables.Dispose();
		_changed.OnCompleted();
	}

	private static string? LabelOf(string? typedLabel)
	{
		return string.IsNullOrWhiteSpace(typedLabel) ? null : typedLabel.Trim();
	}

	// docs/architecture/trend-interaction.md#the-time-markers
	private int NextNumber()
	{
		var highest = 0;
		foreach (var marker in _markerSet.All)
		{
			if (marker.Kind == MarkerKind.User && NumberOf(marker.Label) is { } number)
			{
				highest = Math.Max(highest, number);
			}
		}

		return highest + 1;
	}

	private static int? NumberOf(string? label)
	{
		if (label is null || !label.StartsWith(NumberPrefix, StringComparison.Ordinal))
		{
			return null;
		}

		var digits = label.AsSpan(NumberPrefix.Length);

		return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
	}

	private void ApplyRead(IReadOnlyList<StoredMarker> read)
	{
		if (_markerSet.Apply(read))
		{
			RaiseChanged();
		}
	}

	// Called with the marker already held; a revert waits for the last write on that marker to land.
	private async Task WriteHeldAsync(
		StoredMarker stored,
		Func<Task<Result>> write,
		Func<StoredMarker, StoredMarker> revert)
	{
		Result written;
		bool isReleased;
		try
		{
			written = await write();
		}
		finally
		{
			isReleased = _markerSet.Release(stored.Id);
		}

		if (written.HasError<ArchiveError>(error => error.Kind == ArchiveFault.MarkerGone))
		{
			RemoveMarker(stored.Id);
		}
		else if (written.IsFailed && isReleased)
		{
			ReplaceHeld(stored.Id, revert);
		}
	}

	private void ReplaceHeld(long id, Func<StoredMarker, StoredMarker> replace)
	{
		var current = FindMarker(id);
		if (current is null)
		{
			return;
		}

		var replaced = replace(current);
		if (replaced != current)
		{
			_markerSet.Place(replaced);
			RaiseChanged();
		}
	}

	private void RemoveMarker(long id)
	{
		if (FindMarker(id) is not null)
		{
			_markerSet.Remove(id);
			RaiseChanged();
		}
	}

	private StoredMarker? FindMarker(long id)
	{
		return _markerSet.All.FirstOrDefault(marker => marker.Id == id);
	}

	private void RaiseChanged()
	{
		_changed.OnNext(Unit.Default);
	}

	private readonly record struct MarkerDrag(StoredMarker AtStart, DateTime ReachedUtc);
}
