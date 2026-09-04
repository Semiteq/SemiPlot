using System.ComponentModel;
using System.Diagnostics;

namespace SemiPlot.Tests.Integration;

internal static class DockerCli
{
	public const string ProvisionerTag = "ghcr.io/semiteq/semibase:latest";

	// The builder resolves FROM from the local cache, so this is what moves the tag. A failed pull is
	// reported and the build goes on with the cached image (docs/architecture/bench.md#where-the-provisioning-comes-from).
	public static async Task PullProvisionerAsync(TimeSpan bound)
	{
		var (exitCode, _, error) = await RunAsync(["pull", "--quiet", ProvisionerTag], bound);

		if (exitCode != 0)
		{
			await Console.Error.WriteLineAsync(
				$"[bench] docker pull {ProvisionerTag} failed, building over the cached image: {error}");
		}
	}

	private static async Task<(int ExitCode, string Output, string Error)> RunAsync(
		string[] arguments,
		TimeSpan bound)
	{
		var startInfo = new ProcessStartInfo("docker")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var bounded = new CancellationTokenSource(bound);

		try
		{
			using var process = Process.Start(startInfo)
								?? throw new InvalidOperationException("docker did not start.");

			var output = process.StandardOutput.ReadToEndAsync(bounded.Token);
			var error = process.StandardError.ReadToEndAsync(bounded.Token);

			await process.WaitForExitAsync(bounded.Token);

			return (process.ExitCode, (await output).Trim(), (await error).Trim());
		}
		catch (Win32Exception exception)
		{
			return (-1, string.Empty, $"the docker CLI is not on PATH: {exception.Message}");
		}
		catch (OperationCanceledException)
		{
			return (-1, string.Empty, $"docker {arguments[0]} did not finish within {bound}.");
		}
	}
}
