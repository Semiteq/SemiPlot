using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa;
using SemiPlot.DataSource.OpcUa.Configuration;
using SemiPlot.Tests.Integration.DemoScada;
using SemiPlot.Tools.ArchiveSeeder;

namespace SemiPlot.Tests.Integration.OpcUa;

/// <summary>One state of the source and the moment the test received it.</summary>
internal sealed record ReceivedState(LiveSourceState State, DateTime ReceivedUtc)
{
	public string Name => State.ToString();
}

/// <summary>
/// A live source on one loopback port with a log collector of its own, recording its states, its pen faults and the
/// batches of every subscription it opened as they arrive.
/// </summary>
internal sealed class LiveSourceHost : IAsyncDisposable
{
	/// <summary>For a test about the grace; a first connect on a loaded machine can outlast it.</summary>
	public static readonly TimeSpan ShortGrace = TimeSpan.FromSeconds(1);

	private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(100);

	private readonly string _storeRoot;
	private readonly ILoggerFactory _loggerFactory;
	private readonly Lock _gate = new();
	private readonly List<ReceivedState> _states = [];
	private readonly List<LiveSourceError> _penFaults = [];
	private readonly List<SampleRecorder> _recorders = [];
	private readonly IDisposable _recording;
	private readonly IDisposable _faultRecording;
	private volatile bool _isCompleted;

	/// <summary>A source with the production grace, so a server that is already up writes no fault first.</summary>
	public LiveSourceHost(int port)
		: this(port, OpcUaLiveSource.Grace)
	{
	}

	public LiveSourceHost(int port, TimeSpan grace)
	{
		_storeRoot = Path.Combine(Path.GetTempPath(), "SemiPlot.Tests", $"LiveSource-{Guid.NewGuid():N}");
		_loggerFactory = LoggerFactory.Create(builder => builder
			.AddProvider(new FakeLoggerProvider(Logs))
			.SetMinimumLevel(LogLevel.Information)
			.AddFilter(OpcUaSession.ChannelLogCategory, LogLevel.Critical));
		Source = new OpcUaLiveSource(
			new OpcUaConnectionSettings("127.0.0.1", port, _storeRoot),
			_loggerFactory,
			grace);
		_recording = Source.States.Subscribe(Record, () => _isCompleted = true);
		_faultRecording = Source.PenFaults.Subscribe(Record);
	}

	public static IReadOnlyList<int> DemoPenIds { get; } =
		[.. RawLayerGenerator.SelectPens(DemoScadaHost.Options.PenCount).Select(pen => pen.PenId)];

	public OpcUaLiveSource Source { get; }

	public FakeLogCollector Logs { get; } = new();

	public DateTime StartedUtc { get; private set; }

	public bool IsCompleted => _isCompleted;

	public IReadOnlyList<ReceivedState> States
	{
		get
		{
			lock (_gate)
			{
				return [.. _states];
			}
		}
	}

	public IReadOnlyList<LiveSourceError> PenFaults
	{
		get
		{
			lock (_gate)
			{
				return [.. _penFaults];
			}
		}
	}

	/// <summary>The subscription <see cref="Start(IReadOnlyList{int})"/> opened.</summary>
	public SampleRecorder Primary
	{
		get => field ?? throw new InvalidOperationException("The host opened no subscription yet.");
		private set;
	}

	public static int PortOf(DemoScadaServer server)
	{
		return new Uri(server.EndpointUrl).Port;
	}

	/// <summary>Starts the session the way the coordinator does, by subscribing the demo's pens.</summary>
	public void Start()
	{
		Start(DemoPenIds);
	}

	public void Start(IReadOnlyList<int> penIds)
	{
		StartedUtc = DateTime.UtcNow;
		Primary = Subscribe(penIds);
	}

	/// <summary>Opens one more subscription; the host disposes it with itself.</summary>
	public SampleRecorder Subscribe(IReadOnlyList<int> penIds)
	{
		var recorder = new SampleRecorder(Source.Subscribe(penIds));

		lock (_gate)
		{
			_recorders.Add(recorder);
		}

		return recorder;
	}

	public Task<bool> WaitForStatesAsync(int count, TimeSpan bound)
	{
		return WaitUntilAsync(() => States.Count >= count, bound);
	}

	public static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan bound)
	{
		using var giveUp = new CancellationTokenSource(bound);

		while (!condition())
		{
			try
			{
				await Task.Delay(_pollInterval, giveUp.Token);
			}
			catch (OperationCanceledException)
			{
				return condition();
			}
		}

		return true;
	}

	/// <summary>The states so far and the source's records at Warning and above, for an assertion's reason.</summary>
	public string Describe()
	{
		var states = string.Join(", ", States.Select(state => $"{state.Name}@{state.ReceivedUtc:HH:mm:ss.fff}"));
		var warnings = LogRecordLines.Of(Logs.GetSnapshot().Where(record => record.Level >= LogLevel.Warning));
		var faults = string.Join(", ", PenFaults.Select(fault => fault.Message));

		return $"states [{states}]; pen faults [{faults}]; the source logged [{warnings}]";
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var recorder in _recorders)
		{
			recorder.Dispose();
		}

		await Source.DisposeAsync();
		_recording.Dispose();
		_faultRecording.Dispose();
		_loggerFactory.Dispose();

		if (Directory.Exists(_storeRoot))
		{
			Directory.Delete(_storeRoot, recursive: true);
		}
	}

	private void Record(LiveSourceError fault)
	{
		lock (_gate)
		{
			_penFaults.Add(fault);
		}
	}

	private void Record(LiveSourceState state)
	{
		lock (_gate)
		{
			_states.Add(new ReceivedState(state, DateTime.UtcNow));
		}
	}
}
