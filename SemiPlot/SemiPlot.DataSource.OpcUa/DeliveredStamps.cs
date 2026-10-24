using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.OpcUa;

// docs/architecture/data-integration.md#the-breaks-the-source-writes
/// <summary>The newest value stamp per pen and the breaks it places; only the source's loop calls it.</summary>
internal sealed class DeliveredStamps
{
	private readonly Dictionary<int, PenStamp> _pens = [];

	public void RecordValue(int penId, DateTime stampUtc)
	{
		var newest = _pens.GetValueOrDefault(penId).NewestUtc is { } known && known > stampUtc ? known : stampUtc;
		_pens[penId] = new PenStamp(newest, EndsOnBreak: false);
	}

	/// <summary>The break a <c>Bad</c> notification writes, or null when there is nothing to place it after.</summary>
	public Sample? BadBreak(int penId, DateTime? stampUtc)
	{
		var pen = _pens.GetValueOrDefault(penId);

		if (pen.EndsOnBreak)
		{
			return null;
		}

		var floor = pen.NewestUtc + Sample.BreakStep;
		var breakUtc = (stampUtc, floor) switch
		{
			({ } stamp, { } least) => stamp > least ? stamp : least,
			({ } stamp, null) => stamp,
			(null, { } least) => least,
			_ => (DateTime?)null
		};

		return breakUtc is { } at ? PlaceBreak(penId, pen, at) : null;
	}

	/// <summary>The break a value that turned non-numeric writes, at the later of its stamp and the newest.</summary>
	public Sample? NotNumericBreak(int penId, DateTime? stampUtc)
	{
		if (!_pens.TryGetValue(penId, out var pen)
			|| pen.NewestUtc is not { } newest
			|| pen.EndsOnBreak
			|| stampUtc is not { } stamp)
		{
			return null;
		}

		return PlaceBreak(penId, pen, stamp > newest ? stamp : newest);
	}

	/// <summary>One break per pen with a delivered value whose last emission is not already a break.</summary>
	public IReadOnlyList<Sample> LossBreaks(IEnumerable<int> penIds)
	{
		var breaks = new List<Sample>();

		foreach (var penId in penIds)
		{
			if (!_pens.TryGetValue(penId, out var pen) || pen.EndsOnBreak || pen.NewestUtc is not { } newest)
			{
				continue;
			}

			breaks.Add(PlaceBreak(penId, pen, newest + Sample.BreakStep));
		}

		return breaks;
	}

	private Sample PlaceBreak(int penId, PenStamp pen, DateTime at)
	{
		_pens[penId] = pen with { EndsOnBreak = true };

		return new Sample(penId, at, null);
	}

	/// <summary>A pen that only ever read <c>Bad</c> has no newest value stamp.</summary>
	private readonly record struct PenStamp(DateTime? NewestUtc, bool EndsOnBreak);
}
