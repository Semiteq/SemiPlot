using Docker.DotNet;
using Docker.DotNet.Models;

using DotNet.Testcontainers.Configurations;

using FluentResults;

namespace SemiPlot.Tests.Data.Integration;

internal static class ProvisionerImage
{
	public const string Repository = "ghcr.io/semiteq/semibase";

	public const string Tag = "latest";

	public const string Reference = $"{Repository}:{Tag}";

	// Where bench/Dockerfile copies the binary to, and so where a started container answers for its
	// version.
	public const string ExecutablePath = "/semibase";

	public const string VersionArgument = "--version";

	// Moves the tag, then names the manifest it landed on. `latest` moves under an unchanged commit, so
	// a run that fails tomorrow has to be able to name the provisioner it ran, and the build has to be
	// pinned to the manifest this step resolved rather than to the tag.
	//
	// The bound is applied here rather than by the caller, so a timeout never escapes as
	// OperationCanceledException.
	public static async Task<Result<ProvisionerResolution>> ResolveAsync(
		TimeSpan bound,
		CancellationToken cancellationToken = default)
	{
		// Null when no endpoint provider is both applicable and reachable — a machine with no container
		// runtime, or one whose daemon is stopped. Dereferencing it would make that machine's stated
		// reason a NullReferenceException.
		if (TestcontainersSettings.OS.DockerEndpointAuthConfig is not { } endpoint)
		{
			return Result.Fail<ProvisionerResolution>(
				"no Docker endpoint answered, so the provisioner image cannot be fetched.");
		}

		using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		bounded.CancelAfter(bound);

		try
		{
			using var client = endpoint.GetDockerClientBuilder().Build();

			var pull = await PullAsync(client, bounded.Token);
			var resolved = await DescribeAsync(client, bounded.Token);

			if (resolved.IsFailed)
			{
				// A pull that failed and left nothing behind is the failure worth reporting. The inspect only
				// says the cache is empty, which is that failure's consequence rather than its cause.
				return Result.Fail<ProvisionerResolution>(
					pull.IsFailed ? pull.Errors[0].Message : resolved.Errors[0].Message);
			}

			return Result.Ok(
				new ProvisionerResolution(resolved.Value, pull.IsSuccess ? null : pull.Errors[0].Message));
		}
		// The linked source fired on its own timer, so the caller's token is still unset and the bound
		// becomes a stated reason; a caller who really cancelled sets that token and the exception
		// propagates as before.
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Result.Fail<ProvisionerResolution>(
				$"'{Reference}' did not resolve within {bound}; the registry or the daemon is not answering.");
		}
	}

	private static async Task<Result> PullAsync(IDockerClient client, CancellationToken cancellationToken)
	{
		var failures = new PullFailureLog();

		try
		{
			await client.Images.CreateImageAsync(
				new ImagesCreateParameters { FromImage = Repository, Tag = Tag },
				null,
				failures,
				cancellationToken);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Result.Fail(exception.Message);
		}

		// A pull can also report its failure inside the progress stream instead of throwing, so the
		// stream is the second place one has to be read from.
		return failures.Describe() is { } failure ? Result.Fail(failure) : Result.Ok();
	}

	private static async Task<Result<string>> DescribeAsync(
		IDockerClient client,
		CancellationToken cancellationToken)
	{
		try
		{
			var image = await client.Images.InspectImageAsync(Reference, cancellationToken);

			// The repository digest names the manifest the registry served, which is what identifies one
			// provisioner against another and what the build takes as its `FROM`. The local id stands in for
			// an image that carries no digest, and the builder resolves that too.
			return Result.Ok(image.RepoDigests is { Count: > 0 } digests ? digests[0] : image.ID);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			return Result.Fail<string>($"'{Reference}' is not in the local image cache: {exception.Message}");
		}
	}

	// IProgress<T> by hand rather than Progress<T>: Progress<T> posts its callbacks through the
	// synchronisation context, so a message could land after the pull returned and go unread.
	private sealed class PullFailureLog : IProgress<JSONMessage>
	{
		private readonly List<string> _messages = [];

		public void Report(JSONMessage value)
		{
			if (value.Error?.Message is not { Length: > 0 } message)
			{
				return;
			}

			lock (_messages)
			{
				_messages.Add(message);
			}
		}

		public string? Describe()
		{
			lock (_messages)
			{
				return _messages.Count == 0 ? null : string.Join("; ", _messages);
			}
		}
	}
}
