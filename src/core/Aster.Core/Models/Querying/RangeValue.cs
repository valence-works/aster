namespace Aster.Core.Models.Querying;

/// <summary>
/// Inclusive/exclusive bounds for <see cref="ComparisonOperator.Range"/> predicates.
/// A <see langword="null"/> bound means unbounded on that side.
/// </summary>
/// <param name="Min">The lower bound, or <see langword="null"/> for no lower bound.</param>
/// <param name="Max">The upper bound, or <see langword="null"/> for no upper bound.</param>
/// <param name="IncludeMin">Whether <paramref name="Min"/> is inclusive.</param>
/// <param name="IncludeMax">Whether <paramref name="Max"/> is inclusive.</param>
public sealed record RangeValue(
    object? Min,
    object? Max,
    bool IncludeMin = true,
    bool IncludeMax = true);
