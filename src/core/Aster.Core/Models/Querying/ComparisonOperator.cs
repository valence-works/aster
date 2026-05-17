namespace Aster.Core.Models.Querying;

/// <summary>
/// Comparison operator for filter expressions.
/// </summary>
public enum ComparisonOperator
{
    /// <summary>Exact equality match (case-insensitive for strings).</summary>
    Equals,

    /// <summary>Inverse equality match (case-insensitive for strings).</summary>
    NotEquals,

    /// <summary>Exact equality match against any value in a candidate set.</summary>
    In,

    /// <summary>Substring containment check (strings only).</summary>
    Contains,

    /// <summary>Prefix check (strings only, case-insensitive).</summary>
    StartsWith,

    /// <summary>Range comparison using a <see cref="RangeValue"/> bound value.</summary>
    Range,

    /// <summary>Facet value presence check.</summary>
    Exists,
}
