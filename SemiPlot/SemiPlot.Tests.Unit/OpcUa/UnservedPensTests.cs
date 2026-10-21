using AwesomeAssertions;

using Opc.Ua;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.OpcUa;

using Xunit;

namespace SemiPlot.Tests.Unit.OpcUa;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class UnservedPensTests
{
	private const string Endpoint = "opc.tcp://127.0.0.1:4841";

	private readonly ReportedUnservedSets _reported = new();

	[Fact]
	public void UnknownAndNonNumericPens_ReportAsOneErrorOnceEveryItemHasAnswered()
	{
		var pens = Over(1, 2, 3, 4);

		pens.OnItemStatuses([Served(1), Unknown(2), Served(3), Served(4)]).Should().BeNull();
		pens.OnNotNumeric(3).Should().BeNull();
		pens.OnAnswered(1).Should().BeNull();
		var error = pens.OnAnswered(4);

		AssertUnserved(error, unknown: [2], notNumeric: [3]);
	}

	[Fact]
	public void AnItemNotCreatedAsUnknown_IsUnknown()
	{
		var pens = Over(1, 2);

		pens.OnItemStatuses([Served(1), new ItemStatus(2, Created: false, StatusCodes.BadNodeIdUnknown)])
			.Should().BeNull();
		var error = pens.OnAnswered(1);

		AssertUnserved(error, unknown: [2], notNumeric: []);
	}

	[Fact]
	public void AnItemNotCreatedForAnotherReason_IsNotReportedAsUnknown_AndWaitsForNothing()
	{
		var pens = Over(1, 2, 3);

		pens.OnItemStatuses([Served(1), new ItemStatus(2, Created: false, StatusCodes.BadNodeIdInvalid), Served(3)])
			.Should().BeNull();
		pens.OnAnswered(1).Should().BeNull();
		var error = pens.OnNotNumeric(3);

		AssertUnserved(error, unknown: [], notNumeric: [3]);
	}

	[Fact]
	public void APenServedAgainAfterARecreation_LeavesTheNextReport()
	{
		var pens = Over(1, 2, 3);
		pens.OnItemStatuses([Unknown(1), Served(2), Served(3)]);
		pens.OnNotNumeric(2);
		pens.OnSettleElapsed();

		pens.OnItemStatuses([Served(1), Served(2), Served(3)]).Should().BeNull("a shrunk set is not reported");
		pens.OnValue(2).Should().BeNull("a shrunk set is not reported");
		var error = pens.OnNotNumeric(3);

		AssertUnserved(error, unknown: [], notNumeric: [3]);
	}

	[Fact]
	public void TheSettle_ReportsWhileItemsStillWait()
	{
		var pens = Over(1, 2);
		pens.OnItemStatuses([Unknown(1), Served(2)]);

		var error = pens.OnSettleElapsed();

		AssertUnserved(error, unknown: [1], notNumeric: []);
	}

	[Fact]
	public void ALaterNonNumericPen_ReportsTheGrownSet()
	{
		var pens = Over(2, 3);
		pens.OnItemStatuses([Unknown(2), Served(3)]);
		pens.OnSettleElapsed();

		var error = pens.OnNotNumeric(3);

		AssertUnserved(error, unknown: [2], notNumeric: [3]);
		pens.OnNotNumeric(3).Should().BeNull();
	}

	[Fact]
	public void StatusesReadAfterARecreation_AddTheirUnknownPensAndReportTheGrownSet()
	{
		var pens = Over(1, 2);
		pens.OnItemStatuses([Served(1), Served(2)]);
		pens.OnAnswered(1);
		pens.OnAnswered(2).Should().BeNull();

		var error = pens.OnItemStatuses([Unknown(1), Served(2)]);

		AssertUnserved(error, unknown: [1], notNumeric: []);
	}

	[Fact]
	public void ASetTheProcessAlreadyReported_IsNotReportedAgain()
	{
		var first = Over(1, 2);
		first.OnItemStatuses([Unknown(1), Served(2)]);
		first.OnSettleElapsed().Should().NotBeNull();
		var second = Over(1, 2);
		second.OnItemStatuses([Unknown(1), Served(2)]);

		second.OnSettleElapsed().Should().BeNull();
	}

	[Fact]
	public void AnEmptySet_ReportsNothing()
	{
		var pens = Over(1, 2);
		pens.OnItemStatuses([Served(1), Served(2)]);

		pens.OnAnswered(1).Should().BeNull();
		pens.OnAnswered(2).Should().BeNull();
		pens.OnSettleElapsed().Should().BeNull();
	}

	private static ItemStatus Served(int penId)
	{
		return new ItemStatus(penId, Created: true, StatusCodes.Good);
	}

	private static ItemStatus Unknown(int penId)
	{
		return new ItemStatus(penId, Created: true, StatusCodes.BadNodeIdUnknown);
	}

	private static void AssertUnserved(LiveSourceError? error, int[] unknown, int[] notNumeric)
	{
		error.Should().NotBeNull();
		error.Kind.Should().Be(LiveSourceFault.PensUnserved);
		error.Endpoint.Should().Be(Endpoint);
		error.UnknownPenIds.Should().Equal(unknown);
		error.NotNumericPenIds.Should().Equal(notNumeric);
	}

	private UnservedPens Over(params int[] penIds)
	{
		return new UnservedPens(penIds, Endpoint, _reported);
	}
}
