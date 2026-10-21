using System.Threading.Channels;

using Microsoft.Extensions.Logging;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>Runs queued work one item at a time on one reader, so the state the work touches needs no lock.</summary>
internal sealed class SerialLoop(ILogger logger)
{
	private readonly Channel<Func<Task>> _queue =
		Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });

	/// <summary>A work item that throws is logged and the next one runs.</summary>
	public async Task PumpAsync(CancellationToken token)
	{
		try
		{
			await foreach (var work in _queue.Reader.ReadAllAsync(token).ConfigureAwait(false))
			{
				await RunAsync(work, token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
		}
	}

	/// <summary>Work queued after this is dropped.</summary>
	public void Complete()
	{
		_queue.Writer.TryComplete();
	}

	public void Enqueue(Action work)
	{
		Enqueue(() =>
		{
			work();

			return Task.CompletedTask;
		});
	}

	public void Enqueue(Func<Task> work)
	{
		_queue.Writer.TryWrite(work);
	}

	public Task InvokeAsync(Action work)
	{
		return InvokeAsync(() =>
		{
			work();

			return Task.CompletedTask;
		});
	}

	/// <summary>Completes when the work has run on the loop, with its exception when it threw.</summary>
	public Task InvokeAsync(Func<Task> work)
	{
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Enqueue(async () =>
		{
			try
			{
				await work().ConfigureAwait(false);
				done.TrySetResult();
			}
			catch (Exception exception)
			{
				done.TrySetException(exception);
			}
		});

		return done.Task;
	}

	private async Task RunAsync(Func<Task> work, CancellationToken token)
	{
		try
		{
			await work().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException || !token.IsCancellationRequested)
		{
			logger.LogError(exception, "A work item of the OPC UA source failed; the source goes on");
		}
	}
}
