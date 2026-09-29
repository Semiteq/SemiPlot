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

	/// <summary>A relation or function a statement needs does not exist (42P01, 42883). Detail is every relation the statement touches.</summary>
	TableMissing,

	/// <summary>A table exists without the columns the read names (42703). Detail is the server's message.</summary>
	ShapeUnexpected,

	/// <summary>The server ended a statement (57014): a statement_timeout or an administrator's cancel.</summary>
	QueryTimedOut,

	/// <summary>A run of consecutive poll ticks failed. Detail is the number of failures that raised it.</summary>
	ConnectionLost,

	/// <summary>A constraint refused a written value (23514). Detail is the pen's name.</summary>
	ValueRejected,

	/// <summary>A unique constraint refused a name (23505). Detail is the name asked for.</summary>
	NameTaken,

	/// <summary>The pen or group a write names is gone (23503, or no row touched). Detail is its name.</summary>
	RowGone,

	/// <summary>Any other failure. Detail is the SQLSTATE, or empty when the failure carried none.</summary>
	ReadFailed
}
