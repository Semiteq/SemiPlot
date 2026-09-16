using System.Collections.Specialized;

using Microsoft.Extensions.Logging;

using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.Messages;

/// <summary>The doubles every reporting test needs: a panel that refuses the entry, and two log sinks.</summary>
internal static class ReportingTestDoubles
{
	/// <summary>Makes the panel's bound collection throw on every edit, until the returned handle is disposed.</summary>
	public static IDisposable PoisonEntries(MessagePanelViewModel panel)
	{
		var entries = (INotifyCollectionChanged)panel.Entries;
		entries.CollectionChanged += AlwaysThrow;

		return new Unpoison(entries);
	}

	private static void AlwaysThrow(object? sender, NotifyCollectionChangedEventArgs arguments)
	{
		throw new InvalidOperationException("the bound list threw");
	}

	private sealed class Unpoison(INotifyCollectionChanged entries) : IDisposable
	{
		public void Dispose()
		{
			entries.CollectionChanged -= AlwaysThrow;
		}
	}
}

/// <summary>Keeps every level and exception the code under test wrote, in the order it wrote them.</summary>
internal class RecordingLogger : ILogger
{
	public List<LogLevel> Levels { get; } = [];

	public List<Exception?> Exceptions { get; } = [];

	public IReadOnlyList<Exception> Failures => [.. Exceptions.OfType<Exception>()];

	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		Levels.Add(logLevel);
		Exceptions.Add(exception);
	}
}

internal sealed class RecordingLogger<TCategory> : RecordingLogger, ILogger<TCategory>;

/// <summary>A log sink that refuses, either on every call or only on the first one.</summary>
internal class ThrowingLogger(bool throwsOnce = false) : ILogger
{
	private bool _hasThrown;

	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull
	{
		return null;
	}

	public bool IsEnabled(LogLevel logLevel)
	{
		return true;
	}

	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		if (throwsOnce && _hasThrown)
		{
			return;
		}

		_hasThrown = true;

		throw new InvalidOperationException("the log sink threw");
	}
}

internal sealed class ThrowingLogger<TCategory>(bool throwsOnce = false)
	: ThrowingLogger(throwsOnce), ILogger<TCategory>;
