namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The single place that knows the archive's zone. The archive stores naive local wall-clock time and
/// everything above the provider works in UTC, so this converter sits at the provider edge and translates
/// both ways. It is constructed from the <see cref="TimeZoneInfo"/> the connection loader already
/// resolved, so it carries no error path of its own: neither direction throws.
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
	/// A naive value read from the archive becomes an instant with <see cref="DateTimeKind.Utc"/>. The
	/// <see cref="DateTime.Kind"/> of the input is ignored: an archive value is wall-clock time whatever
	/// the caller stamped on it.
	/// <para>
	/// A skipped local time resolves via <see cref="TimeZoneInfo.BaseUtcOffset"/> instead of throwing.
	/// </para>
	/// <para>Ordering across the transitions: docs/architecture/data-integration.md, Time boundary.</para>
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
	/// A UTC window bound becomes the naive local value a query parameter needs. The
	/// <see cref="DateTime.Kind"/> of the input is ignored the same way <see cref="ToUtc"/> ignores it: a
	/// window bound is an instant whatever the caller stamped on it, so a <c>Local</c> or
	/// <c>Unspecified</c> value is read as UTC rather than converted from the machine's own zone.
	/// <para>
	/// Not injective across the autumn fall-back: a window over the transition narrows
	/// (docs/architecture/data-integration.md, Time boundary).
	/// </para>
	/// </summary>
	public DateTime ToArchiveLocal(DateTime utc)
	{
		var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
		var local = TimeZoneInfo.ConvertTimeFromUtc(instant, _sourceTimeZone);

		return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
	}
}
