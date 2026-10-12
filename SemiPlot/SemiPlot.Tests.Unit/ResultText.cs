using AwesomeAssertions;

using FluentResults;

namespace SemiPlot.Tests.Unit;

/// <summary>Reads a failed <see cref="IResultBase"/> in an assertion message or as its one typed error.</summary>
internal static class ResultText
{
	public static string Describe(IResultBase result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}

	public static TError ErrorOf<TError>(IResultBase result)
		where TError : IError
	{
		result.IsFailed.Should().BeTrue();

		return result.Errors.OfType<TError>().Should().ContainSingle().Subject;
	}
}
