using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data.Errors;

using Xunit;

namespace SemiPlot.Tests.Unit.Errors;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DataErrorTests
{
	[Fact]
	public void EveryPublicErrorTypeIsSealedAndDerivesFromError()
	{
		var errorTypes = typeof(ArchiveError).Assembly
			.GetExportedTypes()
			.Where(type => type.Namespace == typeof(ArchiveError).Namespace)
			.Where(type => type.IsClass)
			.ToList();

		errorTypes.Should().NotBeEmpty();
		errorTypes.Should().AllSatisfy(type => type.IsSealed.Should().BeTrue(type.Name));
		errorTypes.Should().AllSatisfy(type => typeof(IError).IsAssignableFrom(type).Should().BeTrue(type.Name));
		errorTypes.Should().AllSatisfy(type => type.Name.Should().EndWith("Error"));
	}
}
