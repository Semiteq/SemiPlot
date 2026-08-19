namespace SemiPlot.Core.Data.Errors;

/// <summary>
/// Which archive object a failed read found absent. The database case comes from SQLSTATE
/// <c>3D000</c> and names no table; the table case comes from <c>42P01</c> and always names one.
/// </summary>
public enum ArchiveObject
{
	Database,
	Table
}
