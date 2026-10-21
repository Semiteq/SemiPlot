using System.Net;
using System.Net.Sockets;

using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// Opens a loopback socket and no container, so it runs without a container runtime.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ConvergeReadinessTests
{
	[Fact]
	public async Task AServerThatAcceptsAndClosesTheSocketIsWaitedFor()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		using var stopListening = CancellationTokenSource.CreateLinkedTokenSource(
			TestContext.Current.CancellationToken);
		var closing = CloseEveryConnectionAsync(listener, stopListening.Token);
		var options = new ConvergeOptions(
			$"Host=127.0.0.1;Port={port};Database=semiplot_app;Username=scada_writer;Password=x",
			$"Host=127.0.0.1;Port={port};Database=postgres;Username=postgres;Password=x",
			"C:\\config",
			null,
			SeederOptions.DefaultChangeSeconds,
			55875);
		using var giveUp = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		giveUp.CancelAfter(TimeSpan.FromSeconds(2));

		Func<Task> act = () => Converge.RunAsync(options, giveUp.Token);

		await act.Should().ThrowAsync<OperationCanceledException>(
			"a connection the port proxy closes before Postgres listens is the container still starting");
		await stopListening.CancelAsync();
		listener.Stop();
		await closing;
	}

	private static async Task CloseEveryConnectionAsync(TcpListener listener, CancellationToken cancellationToken)
	{
		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				using var client = await listener.AcceptTcpClientAsync(cancellationToken);
			}
		}
		catch (Exception exception) when (exception is OperationCanceledException or SocketException
			or ObjectDisposedException)
		{
		}
	}
}
