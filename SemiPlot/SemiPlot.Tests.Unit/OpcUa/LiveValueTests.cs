using AwesomeAssertions;

using Opc.Ua;

using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class LiveValueTests
{
	private static readonly DateTime _sourceUtc =
		new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc).AddTicks(4567);

	private static readonly DateTime _serverUtc = _sourceUtc.AddMilliseconds(250);

	public static TheoryData<Variant, double> NumericValues => new()
	{
		{ Variant.From((sbyte)-5), -5 },
		{ Variant.From((byte)1), 1 },
		{ Variant.From((short)-300), -300 },
		{ Variant.From((ushort)300), 300 },
		{ Variant.From(-70000), -70000 },
		{ Variant.From(70000u), 70000 },
		{ Variant.From(-5000000000L), -5000000000 },
		{ Variant.From(5000000000UL), 5000000000 },
		{ Variant.From(1.5f), 1.5 },
		{ Variant.From(2.25), 2.25 },
	};

	public static TheoryData<string, Variant> NonNumericValues => new()
	{
		{ "Boolean", Variant.From(true) },
		{ "String", Variant.From("12.5") },
		{ "DateTime", Variant.From(new DateTimeUtc(_sourceUtc)) },
		{ "array", Variant.From([1.0, 2.0]) },
		{ "null", Variant.Null },
	};

	[Theory]
	[MemberData(nameof(NumericValues))]
	public void ANumericBuiltInType_ReadsAsItsDouble(Variant variant, double expected)
	{
		var reading = LiveValue.Read(new DataValue(variant, StatusCodes.Good, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Value);
		reading.Value.Should().Be(expected);
	}

	[Theory]
	[MemberData(nameof(NonNumericValues))]
	public void ANonNumericValue_ReadsAsNotNumeric(string kind, Variant variant)
	{
		var reading = LiveValue.Read(new DataValue(variant, StatusCodes.Good, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.NotNumeric, kind);
	}

	[Fact]
	public void Good_ReadsAsAValueAtTheSourceStamp()
	{
		var reading = LiveValue.Read(new DataValue(Variant.From(2.25), StatusCodes.Good, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Value);
		reading.Value.Should().Be(2.25);
		reading.StampUtc.Should().NotBeNull();
		reading.StampUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
		reading.StampUtc.Value.Ticks.Should().Be(_sourceUtc.Ticks);
	}

	[Fact]
	public void GoodWithInfoBits_ReadsAsAValue()
	{
		var withInfoBits = new StatusCode(0x00000010u);

		var reading = LiveValue.Read(new DataValue(Variant.From(1.5f), withInfoBits, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Value);
		reading.Value.Should().Be(1.5);
	}

	[Fact]
	public void Uncertain_ReadsAsAValue()
	{
		var status = StatusCodes.UncertainLastUsableValue;

		var reading = LiveValue.Read(new DataValue(Variant.From(7), status, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Value);
		reading.Value.Should().Be(7);
	}

	[Fact]
	public void Bad_ReadsAsABreakAtTheSourceStamp()
	{
		var status = StatusCodes.BadCommunicationError;

		var reading = LiveValue.Read(new DataValue(Variant.From(7), status, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Break);
		reading.StampUtc.Should().Be(_sourceUtc);
	}

	[Fact]
	public void BadWithNoStamp_ReadsAsABreakWithNoStamp()
	{
		var reading = LiveValue.Read(DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));

		reading.Kind.Should().Be(LiveReadingKind.Break);
		reading.StampUtc.Should().BeNull();
	}

	[Theory]
	[InlineData(double.NaN)]
	[InlineData(double.PositiveInfinity)]
	[InlineData(double.NegativeInfinity)]
	public void ANonFiniteDouble_ReadsAsABreakAtTheSourceStamp(double value)
	{
		var reading = LiveValue.Read(new DataValue(Variant.From(value), StatusCodes.Good, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Break);
		reading.StampUtc.Should().Be(_sourceUtc);
	}

	[Fact]
	public void ANonFiniteFloat_ReadsAsABreak()
	{
		var reading = LiveValue.Read(new DataValue(Variant.From(float.NaN), StatusCodes.Good, _sourceUtc, _serverUtc));

		reading.Kind.Should().Be(LiveReadingKind.Break);
	}

	[Fact]
	public void NoSourceStamp_TakesTheServerStamp()
	{
		var value = new DataValue(Variant.From(2.25), StatusCodes.Good, DateTimeUtc.MinValue, _serverUtc);

		var reading = LiveValue.Read(value);

		reading.Kind.Should().Be(LiveReadingKind.Value);
		reading.StampUtc!.Value.Ticks.Should().Be(_serverUtc.Ticks);
		reading.StampUtc.Value.Kind.Should().Be(DateTimeKind.Utc);
	}

	[Fact]
	public void NeitherStamp_YieldsNoSample()
	{
		var reading = LiveValue.Read(new DataValue(Variant.From(2.25)));

		reading.Kind.Should().Be(LiveReadingKind.Unstamped);
		reading.StampUtc.Should().BeNull();
	}
}
