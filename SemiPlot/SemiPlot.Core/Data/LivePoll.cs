using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>One answered poll of the live edge, with the samples it read.</summary>
public sealed record LivePoll(DateTime AnsweredAtUtc, IReadOnlyList<Sample> Samples, bool ResumesAfterFailure);
