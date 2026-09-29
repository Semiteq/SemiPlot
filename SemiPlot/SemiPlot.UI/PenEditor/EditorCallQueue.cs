using FluentResults;

namespace SemiPlot.UI.PenEditor;

/// <summary>
/// Runs every pen editor call one after another, in the order issued. Used from the UI thread only, so the
/// tail needs no lock.
/// </summary>
public sealed class EditorCallQueue(Action callSucceeded)
{
	private const ConfigureAwaitOptions AfterAnyOutcome =
		ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing;

	private readonly Action _callSucceeded = callSucceeded;

	private Task _tail = Task.CompletedTask;

	/// <summary>
	/// Starts the call once every call queued before it has finished, whatever their outcome, and invokes the
	/// success callback after a call whose result succeeded.
	/// </summary>
	public Task<T> RunAsync<T>(Func<Task<T>> call)
		where T : IResultBase
	{
		var queued = RunAfterAsync(_tail, call);
		_tail = queued;

		return queued;
	}

	/// <summary>True while no call has been queued after this one.</summary>
	public bool IsLast(Task call)
	{
		return call == _tail;
	}

	/// <summary>Completes once every call queued so far, and every call queued while it waits, has finished.</summary>
	public async Task WhenIdleAsync()
	{
		Task awaited;

		do
		{
			awaited = _tail;
			await awaited.ConfigureAwait(AfterAnyOutcome);
		}
		while (!IsLast(awaited));
	}

	private async Task<T> RunAfterAsync<T>(Task previous, Func<Task<T>> call)
		where T : IResultBase
	{
		await previous.ConfigureAwait(AfterAnyOutcome);

		var result = await call();

		if (result.IsSuccess)
		{
			_callSucceeded();
		}

		return result;
	}
}
