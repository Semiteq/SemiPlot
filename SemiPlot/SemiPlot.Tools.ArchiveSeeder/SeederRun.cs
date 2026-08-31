namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>
/// What a command line asks for: exactly one of <see cref="Seed"/> and <see cref="Follow"/> when
/// <see cref="Errors"/> is empty, neither otherwise.
/// </summary>
public sealed record SeederRun(SeederOptions? Seed, FollowOptions? Follow, IReadOnlyList<string> Errors);
