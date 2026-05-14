namespace Aster.Core.Models.Querying;

/// <summary>
/// Comparison operator for filter expressions.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Exact equality match (case-insensitive for strings).</summary>
    Equals,

    /// <summary>Substring containment check (strings only).</summary>
    Contains,

    /// <summary>Range comparison using a <see cref="RangeValue"/> bound value.</summary>
    Range,
}
