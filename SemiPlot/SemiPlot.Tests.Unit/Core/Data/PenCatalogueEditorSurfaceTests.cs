using AwesomeAssertions;

using SemiPlot.Core.Data;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenCatalogueEditorSurfaceTests
{
	// A property or event would show up among the methods as its accessors.
	[Fact]
	public void TheEditorDeclaresExactlyTheSettingsWritesAndNothingThatManagesAKey()
	{
		var names = typeof(IPenCatalogueEditor).GetMethods().Select(method => method.Name);

		names.Should().BeEquivalentTo(
			"ReadAsync",
			"RegisterNewPensAsync",
			"ChangeAsync",
			"CreateGroupAsync",
			"RenameGroupAsync",
			"DeleteGroupAsync",
			"SetMembershipAsync");
	}
}
