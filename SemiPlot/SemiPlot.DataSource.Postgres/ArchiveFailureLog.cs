using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data.Errors;

namespace SemiPlot.DataSource.Postgres;

internal static class ArchiveFailureLog
{
	/// <summary>
	/// Logs an empty-detail <see cref="ArchiveFault.ReadFailed"/>, a fault in this code, and returns the error.
	/// </summary>
	public static Error LogIfUnexpected(Error error, Exception exception, ILogger logger)
	{
		if (error is ArchiveError { Kind: ArchiveFault.ReadFailed, Detail.Length: 0 })
		{
			logger.LogError(exception, "An archive statement failed with an exception this code did not expect.");
		}

		return error;
	}
}
