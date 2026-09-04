namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The single place that knows the archive's zone: it stores naive local wall-clock time while everything
/// above the provider works in UTC. Built from the <see cref="TimeZoneInfo"/> the connection loader already
/// resolved, so neither direction throws.
/// </summary>
public sealed class ArchiveTimeConverter
{
	private readonly TimeZoneInfo _sourceTimeZone;

	public ArchiveTimeConverter(TimeZoneInfo sourceTimeZone)
	{
		ArgumentNullException.ThrowIfNull(sourceTimeZone);

		_sourceTimeZone = sourceTimeZone;
	}

	/// <summary>
	/// A naive archive value becomes an instant with <see cref="DateTimeKind.Utc"/>; the input's own
	/// <see cref="DateTime.Kind"/> is ignored, and a skipped local time resolves via
	/// <see cref="TimeZoneInfo.BaseUtcOffset"/> instead of throwing (docs/architecture/data-integration.md).
	/// </summary>
	public DateTime ToUtc(DateTime archiveLocal)
	{
		var wallClock = DateTime.SpecifyKind(archiveLocal, DateTimeKind.Unspecified);

		if (_sourceTimeZone.IsInvalidTime(wallClock))
		{
			return DateTime.SpecifyKind(wallClock - _sourceTimeZone.BaseUtcOffset, DateTimeKind.Utc);
		}

		return TimeZoneInfo.ConvertTimeToUtc(wallClock, _sourceTimeZone);
	}

	/// <summary>
	/// A UTC window bound becomes the naive local value a query parameter needs; the input's
	/// <see cref="DateTime.Kind"/> is ignored the same way <see cref="ToUtc"/> ignores it, and the mapping is
	/// not injective across the autumn fall-back (docs/architecture/data-integration.md, Time boundary).
	/// </summary>
	public DateTime ToArchiveLocal(DateTime utc)
	{
		var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
		var local = TimeZoneInfo.ConvertTimeFromUtc(instant, _sourceTimeZone);

		return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
	}
}
