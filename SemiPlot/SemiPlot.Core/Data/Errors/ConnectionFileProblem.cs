namespace SemiPlot.Core.Data.Errors;

public enum ConnectionFileProblem
{
	NotFound,
	Unreadable,
	Unparseable,
	MissingField,
	OutOfRange,
	UnknownTimeZone
}
