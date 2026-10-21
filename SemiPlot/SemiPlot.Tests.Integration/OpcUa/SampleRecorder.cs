using System.Reactive;
using System.Reactive.Linq;

using SemiPlot.Core.Trends;

namespace SemiPlot.Tests.Integration.OpcUa;

/// <summary>One sample and the moment the test received the batch that carried it.</summary>
internal sealed record ReceivedSample(Sample Sample, DateTime ReceivedUtc)
{
	public bool IsBreak => Sample.Value is null;
}

/// <summary>Records one subscription's batches in arrival order, and every completion or fault of its stream.</summary>
internal sealed class SampleRecorder : IDisposable
{
	private readonly Lock _gate = new();
	private readonly List<ReceivedSample> _samples = [];
	private readonly List<string> _terminations = [];
	private readonly IDisposable _subscription;
	private int _batchCount;

	public SampleRecorder(IObservable<IReadOnlyList<Sample>> stream)
	{
		_subscription = stream.Materialize().Subscribe(Record);
	}

	public int BatchCount
	{
		get
		{
			lock (_gate)
			{
				return _batchCount;
			}
		}
	}

	public IReadOnlyList<ReceivedSample> Samples
	{
		get
		{
			lock (_gate)
			{
				return [.. _samples];
			}
		}
	}

	/// <summary>Each completion or fault the stream raised; a live stream raises neither.</summary>
	public IReadOnlyList<string> Terminations
	{
		get
		{
			lock (_gate)
			{
				return [.. _terminations];
			}
		}
	}

	public IReadOnlyList<ReceivedSample> SamplesOf(int penId)
	{
		return [.. Samples.Where(received => received.Sample.PenId == penId)];
	}

	public bool HasValuesForEach(IEnumerable<int> penIds, int count = 1)
	{
		return penIds.All(penId => SamplesOf(penId).Count(received => !received.IsBreak) >= count);
	}

	public bool HasValueAfterABreakForEach(IEnumerable<int> penIds)
	{
		return penIds.All(penId => SamplesOf(penId)
			.SkipWhile(received => !received.IsBreak)
			.Any(received => !received.IsBreak));
	}

	public void Dispose()
	{
		_subscription.Dispose();
	}

	private void Record(Notification<IReadOnlyList<Sample>> notification)
	{
		var receivedUtc = DateTime.UtcNow;

		lock (_gate)
		{
			if (notification.Kind != NotificationKind.OnNext)
			{
				_terminations.Add($"{notification.Kind} {notification.Exception}");

				return;
			}

			_batchCount++;
			_samples.AddRange(notification.Value.Select(sample => new ReceivedSample(sample, receivedUtc)));
		}
	}
}
