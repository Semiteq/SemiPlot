using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// The newest delivered value stamp per pen for the process, across subscriptions, and the breaks it places:
/// <see cref="Sample.BreakStep"/> past that stamp, never two in a row for one pen; only the source's loop calls it
/// (docs/architecture/data-integration.md#the-breaks-the-source-writes).
/// </summary>
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

		if (breakUtc is null)
		{
			return null;
		}

		_pens[penId] = pen with { EndsOnBreak = true };

		return new Sample(penId, breakUtc.Value, null);
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

			_pens[penId] = pen with { EndsOnBreak = true };
			breaks.Add(new Sample(penId, newest + Sample.BreakStep, null));
		}

		return breaks;
	}

	/// <summary>A pen that only ever read <c>Bad</c> has no newest value stamp.</summary>
	private readonly record struct PenStamp(DateTime? NewestUtc, bool EndsOnBreak);
}
