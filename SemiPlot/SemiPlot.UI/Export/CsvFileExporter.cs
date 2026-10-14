using System.Globalization;
using System.Text;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Export;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Export;

/// <summary>docs/architecture/overview.md#the-export-window</summary>
internal sealed class CsvFileExporter(IArchiveRowReader archiveRowReader, CsvDialect dialect)
{
	private const string PartialSuffix = ".partial";
	private const int BufferSize = 64 * 1024;
	private const string SuggestedNameStampFormat = "yyyyMMdd_HHmmss";

	public static string SuggestedFileName(DateTime from, DateTime to)
	{
		var start = from.ToString(SuggestedNameStampFormat, CultureInfo.InvariantCulture);
		var end = to.ToString(SuggestedNameStampFormat, CultureInfo.InvariantCulture);

		return $"semiplot_{start}-{end}.csv";
	}

	public async Task<Result<long>> ExportAsync(
		string path,
		DateTime from,
		DateTime to,
		IReadOnlyList<Pen> pens,
		Action<long> reportRowsWritten,
		CancellationToken cancellationToken)
	{
		var partialPath = path + PartialSuffix;
		Result<long> outcome;

		try
		{
			outcome = await WritePartialAsync(partialPath, from, to, pens, reportRowsWritten, cancellationToken)
				.ConfigureAwait(false);

			if (outcome.IsSuccess)
			{
				File.Move(partialPath, path, overwrite: true);

				return outcome;
			}
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			outcome = Result.Fail<long>(new ExportFileError(path, failure.Message));
		}
		catch (OperationCanceledException)
		{
			if (TryDeletePartial(partialPath) is { } left)
			{
				return Result.Fail<long>(left);
			}

			throw;
		}
		catch (Exception)
		{
			TryDeletePartial(partialPath);

			throw;
		}

		var stuck = TryDeletePartial(partialPath);

		return stuck is null ? outcome : Result.Fail<long>(stuck).WithErrors(outcome.Errors);
	}

	private async Task<Result<long>> WritePartialAsync(
		string partialPath,
		DateTime from,
		DateTime to,
		IReadOnlyList<Pen> pens,
		Action<long> reportRowsWritten,
		CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(
			partialPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
		var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
		await using var writer = new StreamWriter(stream, encoding, BufferSize);

		ArchiveCsvWriter.WriteHeader(writer, dialect);

		var penFields = ArchiveCsvWriter.PenFields(pens, dialect);
		var penIds = pens.Select(pen => pen.PenId).ToArray();
		long total = 0;

		cancellationToken.ThrowIfCancellationRequested();

		var seeds = await archiveRowReader.ReadSeedRowsAsync(penIds, from, cancellationToken).ConfigureAwait(false);

		if (seeds.IsFailed)
		{
			return Result.Fail<long>(seeds.Errors);
		}

		ArchiveCsvWriter.WriteRows(writer, dialect, penFields, seeds.Value);
		total += seeds.Value.Count;
		reportRowsWritten(total);

		foreach (var (sliceFrom, sliceTo) in ArchiveRowSlices.Tile(from, to))
		{
			cancellationToken.ThrowIfCancellationRequested();

			var slice = await archiveRowReader.ReadSliceAsync(penIds, sliceFrom, sliceTo, cancellationToken)
				.ConfigureAwait(false);

			if (slice.IsFailed)
			{
				return Result.Fail<long>(slice.Errors);
			}

			ArchiveCsvWriter.WriteRows(writer, dialect, penFields, slice.Value);
			total += slice.Value.Count;
			reportRowsWritten(total);
		}

		await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

		return total;
	}

	private static ExportFileError? TryDeletePartial(string partialPath)
	{
		try
		{
			File.Delete(partialPath);

			return null;
		}
		catch (DirectoryNotFoundException)
		{
			return null;
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			return new ExportFileError(partialPath, failure.Message, isPartialFile: true);
		}
	}
}
