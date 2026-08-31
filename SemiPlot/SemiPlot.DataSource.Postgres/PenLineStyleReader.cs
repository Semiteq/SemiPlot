using Microsoft.Extensions.Logging;

using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Turns the <c>smallint</c> of <c>semiplot_tags.line_style</c> into a <see cref="PenLineStyle"/>. The
/// stored value is the member's ordinal, written that way by the bench seeder, so the conversion goes
/// through an explicit switch rather than a cast: a cast would accept any number the column can hold and
/// an added member would widen the accepted set by accident, while the switch forces the decision here.
/// <para>
/// An unrecognised value takes <see cref="PenLineStyle.Interpolated"/> instead of failing the read,
/// because one malformed row must not hide every other pen.
/// </para>
/// </summary>
internal static class PenLineStyleReader
{
	public static PenLineStyle Read(short storedValue, int penId, ILogger logger)
	{
		switch (storedValue)
		{
			case 0:
				return PenLineStyle.Interpolated;
			case 1:
				return PenLineStyle.Stepped;
			default:
				logger.LogWarning(
					"Pen {PenId} carries line_style {StoredValue}, which this build does not recognise; "
					+ "it is drawn interpolated.",
					penId,
					storedValue);

				return PenLineStyle.Interpolated;
		}
	}
}
