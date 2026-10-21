using Opc.Ua;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>What one notification of a pen's item means for its line.</summary>
internal enum LiveReadingKind
{
	/// <summary>A number at <see cref="LiveReading.StampUtc"/>.</summary>
	Value,

	/// <summary>
	/// A <c>Bad</c> status or a NaN or infinity: a break at <see cref="LiveReading.StampUtc"/>, if any.
	/// </summary>
	Break,

	/// <summary>A value that is not a number: the pen cannot be drawn.</summary>
	NotNumeric,

	/// <summary>A value with neither a source nor a server stamp: nothing to place.</summary>
	Unstamped
}

/// <summary>
/// One notification mapped; <see cref="Value"/> is meaningful only for <see cref="LiveReadingKind.Value"/>.
/// </summary>
internal readonly record struct LiveReading(LiveReadingKind Kind, DateTime? StampUtc, double Value);

/// <summary>Maps the stack's data values onto the chart's numbers and true-UTC stamps; no zone conversion.</summary>
internal static class LiveValue
{
	/// <summary><c>Good</c> and <c>Uncertain</c> draw the value; <c>Bad</c>, NaN and an infinity are a break.</summary>
	public static LiveReading Read(DataValue dataValue)
	{
		var stamp = StampOf(dataValue);

		if (dataValue.IsBad)
		{
			return new LiveReading(LiveReadingKind.Break, stamp, double.NaN);
		}

		if (ToDouble(dataValue.WrappedValue) is not { } value)
		{
			return new LiveReading(LiveReadingKind.NotNumeric, stamp, double.NaN);
		}

		if (!double.IsFinite(value))
		{
			return new LiveReading(LiveReadingKind.Break, stamp, double.NaN);
		}

		return stamp is null
			? new LiveReading(LiveReadingKind.Unstamped, null, double.NaN)
			: new LiveReading(LiveReadingKind.Value, stamp, value);
	}

	private static double? ToDouble(Variant variant)
	{
		if (variant.IsNull || !variant.TypeInfo.IsScalar)
		{
			return null;
		}

		return variant.TypeInfo.BuiltInType switch
		{
			BuiltInType.SByte or BuiltInType.Byte or BuiltInType.Int16 or BuiltInType.UInt16
				or BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Int64 or BuiltInType.UInt64
				or BuiltInType.Float or BuiltInType.Double
				=> variant.ConvertToDouble().GetDouble(double.NaN),
			_ => null
		};
	}

	private static DateTime? StampOf(DataValue dataValue)
	{
		if (dataValue.SourceTimestamp != DateTimeUtc.MinValue)
		{
			return dataValue.SourceTimestamp.ToDateTime();
		}

		if (dataValue.ServerTimestamp != DateTimeUtc.MinValue)
		{
			return dataValue.ServerTimestamp.ToDateTime();
		}

		return null;
	}
}
