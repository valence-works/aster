namespace Aster.Core.Models.Querying;

/// <summary>
/// Comparison operator for filter expressions.
/// </summary>
/// <remarks>
/// <c>Range</c> is included in the AST contract but the Phase 1 in-memory evaluator
/// (<c>InMemoryQueryService</c>) throws <see cref="NotSupportedException"/> for Range queries.
/// Phase 1 supports <see cref="Equals"/> and <see cref="Contains"/> only (spec §6).
/// </remarks>
public enum ComparisonOperator
{
    /// <summary>Exact equality match (case-insensitive for strings).</summary>
    Equals,

    /// <summary>Substring containment check (strings only).</summary>
    Contains,

    /// <summary>
    /// Range comparison (min/max bounds).
    /// <strong>Not supported</strong> by the Phase 1 in-memory evaluator — throws <see cref="NotSupportedException"/>.
    /// </summary>
    Range,
}
