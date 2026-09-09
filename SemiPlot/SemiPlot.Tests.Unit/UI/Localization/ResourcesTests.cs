using System.Reflection;

using AwesomeAssertions;

using SemiPlot.UI.Localization;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Localization;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ResourcesTests
{
	[Fact]
	public void EveryGeneratedAccessor_ResolvesToANonEmptyValue()
	{
		var accessors = typeof(Resources)
			.GetProperties(BindingFlags.Public | BindingFlags.Static)
			.Where(property => property.PropertyType == typeof(string))
			.ToList();

		accessors.Should().NotBeEmpty();

		foreach (var accessor in accessors)
		{
			var value = (string?)accessor.GetValue(null);
			value.Should().NotBeNullOrWhiteSpace("'{0}' is read by the operator", accessor.Name);
		}
	}

	[Fact]
	public void GlyphValues_KeepTheCodePointsTheOperatorSees()
	{
		Resources.NoValuePlaceholder.Should().Be("\u2014");
		Resources.DeltaTimeLabel.Should().Be("\u0394t");
		Resources.DeltaValueLabel.Should().Be("\u0394y");
	}

	[Fact]
	public void CompositeFormats_KeepTheirPlaceholder()
	{
		Resources.ToolbarLayerFormat.Should().Contain("{0}");
		Resources.StatusPenCountFormat.Should().Contain("{0}");
	}
}
