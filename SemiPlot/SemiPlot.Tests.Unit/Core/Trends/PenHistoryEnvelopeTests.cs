using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class PenHistoryEnvelopeTests
{
	private static readonly DateTime _origin = new(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Construct_AscendingTimestampsAndEqualLengths_Succeeds()
	{
		var timestamps = new[] { _origin, _origin.AddSeconds(1), _origin.AddSeconds(2) };
		var min = new[] { 0.0, 1.0, 2.0 };
		var max = new[] { 1.0, 2.0, 3.0 };
		var center = new[] { 0.5, 1.5, 2.5 };

		var act = () => new PenHistoryEnvelope(1, timestamps, min, max, center);

		act.Should().NotThrow();
	}

	[Fact]
	public void Construct_EmptyColumns_Succeeds()
	{
		var act = () => new PenHistoryEnvelope(
			1,
			Array.Empty<DateTime>(),
			Array.Empty<double>(),
			Array.Empty<double>(),
			Array.Empty<double>());

		act.Should().NotThrow();
	}

	[Fact]
	public void Construct_SingleElement_Succeeds()
	{
		var act = () => new PenHistoryEnvelope(
			1,
			new[] { _origin },
			new[] { 0.0 },
			new[] { 1.0 },
			new[] { 0.5 });

		act.Should().NotThrow();
	}

	[Fact]
	public void Construct_MismatchedColumnLengths_Throws()
	{
		var timestamps = new[] { _origin, _origin.AddSeconds(1) };
		var min = new[] { 0.0, 1.0 };
		var max = new[] { 1.0 };
		var center = new[] { 0.5, 1.5 };

		var act = () => new PenHistoryEnvelope(1, timestamps, min, max, center);

		act.Should().Throw<ArgumentException>().WithParameterName("center");
	}

	[Fact]
	public void Construct_NonAscendingTimestamps_Throws()
	{
		var timestamps = new[] { _origin, _origin.AddSeconds(2), _origin.AddSeconds(1) };
		var min = new[] { 0.0, 1.0, 2.0 };
		var max = new[] { 1.0, 2.0, 3.0 };
		var center = new[] { 0.5, 1.5, 2.5 };

		var act = () => new PenHistoryEnvelope(1, timestamps, min, max, center);

		act.Should().Throw<ArgumentException>().WithParameterName("timestamps");
	}

	[Fact]
	public void Construct_DuplicateTimestamps_Throws()
	{
		var timestamps = new[] { _origin, _origin };
		var min = new[] { 0.0, 1.0 };
		var max = new[] { 1.0, 2.0 };
		var center = new[] { 0.5, 1.5 };

		var act = () => new PenHistoryEnvelope(1, timestamps, min, max, center);

		act.Should().Throw<ArgumentException>().WithParameterName("timestamps");
	}

	[Fact]
	public void Construct_NullTimestamps_Throws()
	{
		var act = () => new PenHistoryEnvelope(
			1,
			null!,
			Array.Empty<double>(),
			Array.Empty<double>(),
			Array.Empty<double>());

		act.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void Construct_NullMin_Throws()
	{
		var act = () => new PenHistoryEnvelope(
			1,
			Array.Empty<DateTime>(),
			null!,
			Array.Empty<double>(),
			Array.Empty<double>());

		act.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void Construct_NullMax_Throws()
	{
		var act = () => new PenHistoryEnvelope(
			1,
			Array.Empty<DateTime>(),
			Array.Empty<double>(),
			null!,
			Array.Empty<double>());

		act.Should().Throw<ArgumentNullException>();
	}

	[Fact]
	public void Construct_NullCenter_Throws()
	{
		var act = () => new PenHistoryEnvelope(
			1,
			Array.Empty<DateTime>(),
			Array.Empty<double>(),
			Array.Empty<double>(),
			null!);

		act.Should().Throw<ArgumentNullException>();
	}
}
