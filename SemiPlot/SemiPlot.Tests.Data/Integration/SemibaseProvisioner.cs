using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using FluentResults;

namespace SemiPlot.Tests.Data.Integration;

// `semibase create` provisions the container the same way it provisions a site: the archive database,
// scada_writer, semiplot_reader, the grants, the default-privileges chain and semiplot_tags. This
// repository defines none of them.
//
// Every semibase command checks before it creates, so re-running one against a provisioned server is
// safe — which is what the SEMIPLOT_TEST_PG path relies on.
public static class SemibaseProvisioner
{
	public const string CreateCommand = "create";

	public const string WriterRole = "scada_writer";

	public const string ReaderRole = "semiplot_reader";

	// Passwords travel through the environment rather than through flags, so they never appear in a
	// process listing.
	public const string SuperPasswordVariable = "SEMIBASE_SUPER_PASSWORD";

	public const string WriterPasswordVariable = "SEMIBASE_WRITER_PASSWORD";

	public const string ReaderPasswordVariable = "SEMIBASE_READER_PASSWORD";

	private static readonly TimeSpan _runTimeout = TimeSpan.FromMinutes(2);

	public static Task<Result<string>> CreateAsync(
		PostgresServer postgresServer,
		string database,
		CancellationToken cancellationToken = default)
	{
		string[] arguments =
		[
			CreateCommand,
			"--host",
			postgresServer.Host,
			"--port",
			postgresServer.Port.ToString(CultureInfo.InvariantCulture),
			"--database",
			database,
			"--superuser",
			postgresServer.Superuser
		];

		var environment = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[SuperPasswordVariable] = postgresServer.SuperuserPassword,
			[WriterPasswordVariable] = postgresServer.WriterPassword,
			[ReaderPasswordVariable] = postgresServer.ReaderPassword
		};

		return RunAsync(postgresServer.SemibaseExecutable, arguments, environment, cancellationToken);
	}

	public static async Task<Result<string>> RunAsync(
		string executable,
		IReadOnlyList<string> arguments,
		IReadOnlyDictionary<string, string>? environment = null,
		CancellationToken cancellationToken = default)
	{
		var startInfo = new ProcessStartInfo(executable)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		foreach (var variable in environment ?? new Dictionary<string, string>(StringComparer.Ordinal))
		{
			startInfo.Environment[variable.Key] = variable.Value;
		}

		try
		{
			return await RunAsync(startInfo, cancellationToken);
		}
		catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
		{
			return Result.Fail<string>($"'{executable}' did not run: {exception.Message}");
		}
	}

	private static async Task<Result<string>> RunAsync(
		ProcessStartInfo startInfo,
		CancellationToken cancellationToken)
	{
		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException($"'{startInfo.FileName}' did not start.");

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		deadline.CancelAfter(_runTimeout);

		// Both pipes are read before the wait: a child that fills one of them while nobody drains it
		// never exits, and the wait would then never return.
		var standardOutput = process.StandardOutput.ReadToEndAsync(deadline.Token);
		var standardError = process.StandardError.ReadToEndAsync(deadline.Token);

		try
		{
			await process.WaitForExitAsync(deadline.Token);
		}
		catch (OperationCanceledException)
		{
			// Both cancellation paths take the same cleanup. A caller that cancels orphans the child just
			// as a timeout does, and both pipe reads are linked to the same token, so a faulted task
			// nobody awaits surfaces later as an unobserved exception in an unrelated test.
			process.Kill(entireProcessTree: true);

			await process.WaitForExitAsync(CancellationToken.None);
			await ObserveAsync(standardOutput);
			await ObserveAsync(standardError);

			// Only the timeout is this method's own failure to report; the caller's cancellation is the
			// caller's, and it travels back as the exception it already is.
			cancellationToken.ThrowIfCancellationRequested();

			return Result.Fail<string>(
				$"'{startInfo.FileName}' did not finish within {_runTimeout.TotalSeconds:0} s.");
		}

		var output = await standardOutput;
		var error = await standardError;

		return process.ExitCode == 0
			? Result.Ok(output)
			: Result.Fail<string>(
				$"'{startInfo.FileName}' exited with {process.ExitCode}: {Describe(output, error)}");
	}

	private static async Task ObserveAsync(Task<string> pipe)
	{
		try
		{
			await pipe;
		}
		catch (Exception exception)
			when (exception is OperationCanceledException or IOException or ObjectDisposedException)
		{
			// The pipe of a killed process carries nothing worth reporting; the timeout is the message.
		}
	}

	private static string Describe(string output, string error)
	{
		var parts = new[] { error.Trim(), output.Trim() }.Where(part => part.Length > 0);

		return string.Join(" / ", parts) is { Length: > 0 } described ? described : "no output";
	}
}
