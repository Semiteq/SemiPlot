using System.Reactive.Concurrency;
using System.Reactive.Disposables;

using Microsoft.Extensions.Logging;

using Opc.Ua;
using Opc.Ua.Client;

using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// One <c>Subscribe</c> call: its OPC UA subscription on the current session, the samples and breaks it emits and
/// the pens it cannot serve. Every member but <see cref="Close"/> runs on the source's loop.
/// </summary>
internal sealed class PenSubscription(
	IReadOnlyList<int> penIds,
	IObserver<IReadOnlyList<Sample>> observer,
	PenSubscriptionContext context)
{
	// A created item that has not answered by then is reported as it stands.
	private static readonly TimeSpan _settleTime = TimeSpan.FromSeconds(5);

	private readonly UnservedPens _unserved = new(penIds, context.Endpoint, context.ReportedSets);
	private IDisposable _settleTimer = Disposable.Empty;

	// Set from the subscriber's thread; read on the loop and on the stack's notification threads.
	private volatile bool _isClosed;

	/// <summary>
	/// The OPC UA subscription on the current session; null until created and after the session ends.
	/// </summary>
	public Subscription? Subscription { get; private set; }

	public bool IsClosed => _isClosed;

	public Subscription Build(string projectId, ushort namespaceIndex)
	{
		var subscription = SubscriptionPlan.CreateSubscription(context.Telemetry);

		foreach (var penId in penIds)
		{
			var item = SubscriptionPlan.CreateItem(context.Telemetry, projectId, namespaceIndex, penId);
			item.Notification += (_, arguments) => OnNotification(subscription, penId, arguments);
			subscription.AddItem(item);
		}

		return subscription;
	}

	/// <summary>Takes a subscription the server has created and starts the settle time of its unserved pens.</summary>
	public void Attach(Subscription subscription)
	{
		Subscription = subscription;
		ReadItemStatuses();
		_settleTimer.Dispose();
		_settleTimer = DefaultScheduler.Instance.Schedule(
			_settleTime,
			() => context.Loop.Enqueue(() => Report(_unserved.OnSettleElapsed())));
	}

	/// <summary>Lets go of the subscription; its notifications emit nothing from here on.</summary>
	public Subscription? Detach()
	{
		var subscription = Subscription;
		Subscription = null;
		_settleTimer.Dispose();

		return subscription;
	}

	/// <summary>
	/// Unknown items join the unserved pens: after a creation and after the stack recreated the session.
	/// </summary>
	public void ReadItemStatuses()
	{
		if (Subscription is null)
		{
			return;
		}

		var statuses = new List<ItemStatus>();

		foreach (var item in Subscription.MonitoredItems)
		{
			if (item.Handle is not int penId)
			{
				continue;
			}

			var status = item.Status.Error?.StatusCode ?? StatusCodes.Good;
			statuses.Add(new ItemStatus(penId, item.Created, status));

			if (StatusCode.IsBad(status) && status != StatusCodes.BadNodeIdUnknown)
			{
				context.Logger.LogWarning(
					"The OPC UA item of pen {PenId} at {Endpoint} was not created: {Status}",
					penId,
					context.Endpoint,
					status);
			}
		}

		Report(_unserved.OnItemStatuses(statuses));
	}

	/// <summary>The session left Connected: one break per pen past its newest delivered stamp.</summary>
	public void EmitLossBreaks()
	{
		var breaks = context.Stamps.LossBreaks(penIds);

		if (breaks.Count == 0)
		{
			return;
		}

		context.Logger.LogDebug("The OPC UA session was lost: {Count} breaks for pens {PenIds}", breaks.Count, penIds);
		Emit(breaks);
	}

	/// <summary>The subscriber let go; nothing is emitted from here on.</summary>
	public void Close()
	{
		_isClosed = true;
	}

	private void OnNotification(Subscription subscription, int penId, MonitoredItemNotificationEventArgs arguments)
	{
		if (!_isClosed && arguments.NotificationValue is MonitoredItemNotification notification)
		{
			context.Loop.Enqueue(() => Deliver(subscription, penId, notification.Value));
		}
	}

	private void Deliver(Subscription subscription, int penId, DataValue dataValue)
	{
		if (!ReferenceEquals(subscription, Subscription))
		{
			return;
		}

		var reading = LiveValue.Read(dataValue);

		switch (reading.Kind)
		{
			case LiveReadingKind.Value when reading.StampUtc is { } stamp:
				context.Stamps.RecordValue(penId, stamp);
				Emit([new Sample(penId, stamp, reading.Value)]);
				Report(_unserved.OnValue(penId));
				break;
			case LiveReadingKind.Break:
				EmitBreak(context.Stamps.BadBreak(penId, reading.StampUtc), "reads Bad");
				Report(_unserved.OnAnswered(penId));
				break;
			case LiveReadingKind.NotNumeric:
				EmitBreak(context.Stamps.NotNumericBreak(penId, reading.StampUtc), "turned non-numeric");
				Report(_unserved.OnNotNumeric(penId));
				break;
			default:
				context.Logger.LogDebug("Pen {PenId} sent a value with neither a source nor a server stamp", penId);
				Report(_unserved.OnAnswered(penId));
				break;
		}
	}

	private void EmitBreak(Sample? placed, string reason)
	{
		if (placed is null)
		{
			return;
		}

		context.Logger.LogDebug(
			"Pen {PenId} {Reason}: a break at {Stamp:O}", placed.PenId, reason, placed.TimestampUtc);
		Emit([placed]);
	}

	private void Emit(IReadOnlyList<Sample> samples)
	{
		if (!_isClosed)
		{
			observer.OnNext(samples);
		}
	}

	private void Report(LiveSourceError? unserved)
	{
		if (unserved is null || _isClosed)
		{
			return;
		}

		context.Logger.LogWarning(
			"Pens unserved by {Endpoint}: unknown {UnknownPenIds}, not numeric {NotNumericPenIds}",
			unserved.Endpoint,
			unserved.UnknownPenIds,
			unserved.NotNumericPenIds);
		context.PenFaults.OnNext(unserved);
	}
}
