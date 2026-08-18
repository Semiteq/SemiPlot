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
	/// Total by construction: a skipped local time — the hour that does not exist at the spring-forward
	/// transition — takes <see cref="TimeZoneInfo.BaseUtcOffset"/> instead of throwing, which places it
	/// deterministically just past the gap. That is the zone's standard-time offset for every zone whose
	/// daylight saving is positive; a zone modelled with negative daylight saving, such as
	/// <c>Europe/Dublin</c> under tzdata, resolves a skipped hour to a different instant than the same
	/// zone read from the Windows registry, and no zone of that shape is in use here. An ambiguous local
	/// time needs no branch, because standard time is already what
	/// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> resolves it to. A misconfigured
	/// or changed source zone puts real archive rows inside the gap, and a throw there would cross the
	/// provider boundary mid-query where no public error type fits.
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
	/// Total by construction: every instant has exactly one local reading. It is not injective, though —
	/// across the autumn fall-back the repeated hour maps two instants onto one naive value, so a UTC
	/// window spanning the transition becomes a narrower local window, and a one-hour window over the
	/// transition itself becomes a zero-width one that selects no rows. Pinned by test; the slice that
	/// builds history queries owns what to do about it.
	/// </para>
	/// </summary>
	public DateTime ToArchiveLocal(DateTime utc)
	{
		var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
		var local = TimeZoneInfo.ConvertTimeFromUtc(instant, _sourceTimeZone);

		return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
	}
}
