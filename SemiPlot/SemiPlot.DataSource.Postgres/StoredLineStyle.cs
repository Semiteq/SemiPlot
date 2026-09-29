using Microsoft.Extensions.Logging;

using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

// The stored value is the member's ordinal. An unrecognised value draws interpolated rather than failing the
// read: one malformed row must not hide every other pen.
internal static class StoredLineStyle
{
	/// <summary>An unrecognised value is logged at the level <paramref name="level"/> gives, asked only then.</summary>
	public static PenLineStyle Read(short storedValue, int penId, ILogger logger, Func<LogLevel> level)
	{
		if (Enum.IsDefined((PenLineStyle)storedValue))
		{
			return (PenLineStyle)storedValue;
		}

		logger.Log(
			level(),
			"Pen {PenId} carries line_style {StoredValue}, which this build does not recognise; "
			+ "it is drawn interpolated.",
			penId,
			storedValue);

		return PenLineStyle.Interpolated;
	}
}
