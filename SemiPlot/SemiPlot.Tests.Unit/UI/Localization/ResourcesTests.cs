using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using AwesomeAssertions;

using SemiPlot.UI.Localization;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Localization;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ResourcesTests
{
	// An escaped brace carries no index, so the doubled forms are matched first and dropped.
	private static readonly Regex _placeholderPattern = new(@"\{\{|\}\}|\{(\d+)");

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
	public void TheCsvHeaderNames_ReadInTheLanguageOfTheirSet()
	{
		var neutral = ReadSet(CultureInfo.InvariantCulture);
		var russian = ReadSet(RussianCulture);

		neutral["ExportCsvHeaderPen"].Should().Be("Pen");
		neutral["ExportCsvHeaderTime"].Should().Be("Time");
		neutral["ExportCsvHeaderValue"].Should().Be("Value");
		russian["ExportCsvHeaderPen"].Should().Be("\u041F\u0435\u0440\u043E");
		russian["ExportCsvHeaderTime"].Should().Be("\u0412\u0440\u0435\u043C\u044F");
		russian["ExportCsvHeaderValue"].Should().Be("\u0417\u043D\u0430\u0447\u0435\u043D\u0438\u0435");
	}

	[Fact]
	public void TheExportDateFormat_IsDigitsAndLiteralSeparatorsPerLanguage()
	{
		ReadSet(CultureInfo.InvariantCulture)["ExportDateFormat"].Should().Be("yyyy-MM-dd");
		ReadSet(RussianCulture)["ExportDateFormat"].Should().Be("dd.MM.yyyy");
	}

	[Fact]
	public void TheExportProgressLine_ReadsWrittenXOfN_InBothLanguages()
	{
		ReadSet(CultureInfo.InvariantCulture)["ExportProgress"].Should().Be("Written {0} of {1}");
		ReadSet(RussianCulture)["ExportProgress"].Should().Be("\u0417\u0430\u043F\u0438\u0441\u0430\u043D\u043E {0} \u0438\u0437 {1}");
	}

	[Fact]
	public void CompositeFormats_KeepTheirPlaceholder()
	{
		Resources.StatusLayerFormat.Should().Contain("{0}");
		Resources.SettingsFieldRequired.Should().Contain("{0}");
		Resources.ExportMessageStartUnreadable.Should().Contain("{0}");
		Resources.ExportMessageEndUnreadable.Should().Contain("{0}");
		Resources.ExportPensHeader.Should().Contain("{0}").And.Contain("{1}");
	}

	[Fact]
	public void TheRussianSet_CarriesEveryKeyTheNeutralSetCarries()
	{
		var neutral = ReadSet(CultureInfo.InvariantCulture);
		var russian = ReadSet(RussianCulture);

		russian.Keys.Should().BeEquivalentTo(neutral.Keys);
	}

	[Fact]
	public void TheRussianSet_ResolvesEveryKeyToANonEmptyValue()
	{
		foreach (var entry in ReadSet(RussianCulture))
		{
			entry.Value.Should().NotBeNullOrWhiteSpace("'{0}' is read by the operator", entry.Key);
		}
	}

	// Three keys are glyphs and share their value by design; every other key has to differ.
	[Fact]
	public void EveryRussianValue_DiffersFromTheNeutralOneOutsideTheGlyphKeys()
	{
		var neutral = ReadSet(CultureInfo.InvariantCulture);
		var russian = ReadSet(RussianCulture);
		string[] glyphKeys = ["DeltaTimeLabel", "DeltaValueLabel", "NoValuePlaceholder"];

		foreach (var entry in russian.Where(entry => !glyphKeys.Contains(entry.Key)))
		{
			entry.Value.Should().NotBe(neutral[entry.Key], "'{0}' is read by the operator", entry.Key);
		}

		foreach (var key in glyphKeys)
		{
			russian[key].Should().Be(neutral[key]);
		}
	}

	// string.Format resolves an index against the argument list the neutral value fixed, so a Russian
	// value that drops one loses text and one that adds one throws out of ArchiveFailureMapper.
	[Fact]
	public void EveryRussianValue_UsesThePlaceholderIndicesOfTheNeutralOne()
	{
		var neutral = ReadSet(CultureInfo.InvariantCulture);
		var russian = ReadSet(RussianCulture);

		foreach (var entry in neutral)
		{
			PlaceholderIndicesOf(russian[entry.Key])
				.Should()
				.BeEquivalentTo(
					PlaceholderIndicesOf(entry.Value),
					"'{0}' is read by the operator",
					entry.Key);
		}
	}

	[Fact]
	public void TheUiCulture_SelectsTheRussianSet()
	{
		using var culture = new CultureScope(RussianCulture.Name);

		Resources.NavigationJumpToNow.Should().Be(ReadSet(RussianCulture)["NavigationJumpToNow"]);
		Resources.NavigationJumpToNow.Should().NotBe(ReadSet(CultureInfo.InvariantCulture)["NavigationJumpToNow"]);
	}

	private static CultureInfo RussianCulture => new("ru");

	private static HashSet<int> PlaceholderIndicesOf(string value)
	{
		return
		[
			.. _placeholderPattern
				.Matches(value)
				.Where(match => match.Groups[1].Success)
				.Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)),
		];
	}

	private static Dictionary<string, string> ReadSet(CultureInfo culture)
	{
		var set = Resources.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
		set.Should().NotBeNull("the '{0}' resource set has to load", culture.Name);

		return set!
			.Cast<DictionaryEntry>()
			.ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
	}
}
