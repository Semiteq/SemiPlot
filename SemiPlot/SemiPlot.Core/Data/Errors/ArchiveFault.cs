namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// What went wrong with the archive, as the operator needs it routed. Each member has one remedy; the
/// SQLSTATEs behind it are the provider's business.
/// </summary>
public enum ArchiveFault
{
	/// <summary>No connection: a refused or reset socket, a client bound firing, a host that does not answer.</summary>
	Unreachable,

	/// <summary>The server answered and refused the credentials or a grant (28P01, 28000, 42501). Detail is the username.</summary>
	AccessDenied,

	/// <summary>The server answers but holds no such database (3D000).</summary>
	DatabaseMissing,

	/// <summary>The database exists but a table the read needs does not (42P01). Detail is the relation.</summary>
	TableMissing,

	/// <summary>A table exists without the columns the read names (42703). Detail is the server's message.</summary>
	ShapeUnexpected,

	/// <summary>The server ended the read (57014): its statement_timeout passed or an administrator cancelled it.</summary>
	QueryTimedOut,

	/// <summary>A run of consecutive poll ticks failed. Detail is the number of failures that raised it.</summary>
	ConnectionLost,

	/// <summary>Any other failure. Detail is the SQLSTATE, or empty when the failure carried none.</summary>
	ReadFailed
}
