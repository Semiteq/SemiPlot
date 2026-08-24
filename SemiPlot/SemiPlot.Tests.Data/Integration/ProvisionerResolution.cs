namespace SemiPlot.Tests.Data.Integration;

// The provisioner image one run built over: the digest that names the manifest, the version its
// binary reported, and — when the pull failed and the local cache stood in — why that digest may sit
// behind the tag.
internal sealed record ProvisionerResolution(string Digest, string? StalenessReason = null)
{
	public string? Version { get; init; }

	public string Describe()
	{
		var named = Version is { Length: > 0 } version
			? $"{ProvisionerImage.Reference} {version} ({Digest})"
			: $"{ProvisionerImage.Reference} ({Digest})";

		return StalenessReason is { } reason
			? $"{named}, kept from the local image cache: {reason}"
			: named;
	}
}
