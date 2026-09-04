namespace SemiPlot.Tools.ArchiveSeeder;

public sealed record ArchiveRow(int Id, short Layer, DateTime Timestamp, double Value, int Quality)
{
	public const short RawLayer = 0;
	public const int OrdinaryQuality = 0;

	// docs/architecture/scada-archive.md#quality-and-gaps. Both marker rows carry a real value; the code
	// only adds the boundary of a gap to a sample that is otherwise ordinary.
	public const int FirstAfterBreakQuality = 16;
	public const int LastBeforeBreakQuality = 32;

	// The column is 'timestamp(3) without time zone' while .NET carries 100 ns ticks, so two rows
	// distinct in memory would collide on (id, l, t) once PostgreSQL rounds them. Truncating here
	// makes an in-memory uniqueness check mean what the primary key means.
	public DateTime Timestamp
	{
		get;
		init => field = TruncateToMilliseconds(value);
	} = TruncateToMilliseconds(Timestamp);

	public static DateTime TruncateToMilliseconds(DateTime timestamp)
	{
		return new DateTime(
			timestamp.Ticks - (timestamp.Ticks % TimeSpan.TicksPerMillisecond),
			timestamp.Kind);
	}
}
