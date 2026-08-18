using FluentResults;

namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// Temporary: reported by a data provider whose member is still a scaffold. Removed once the last
/// member is implemented, which is the slice that implements Subscribe.
/// </summary>
public sealed class ProviderNotImplementedError(string memberName)
	: Error($"The data provider member '{memberName}' is not implemented.")
{
	public string MemberName { get; } = memberName;
}
