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

    /// <summary>
    /// Facet value presence check. A missing value, a JSON <c>null</c>, and an empty array do not exist.
    /// </summary>
    Exists,

    /// <summary>
    /// True when the facet's array contains an element equal to the single candidate value.
    /// Element equality follows <see cref="QueryArrayValue"/>; a non-array facet value never matches.
    /// </summary>
    ArrayContains,

    /// <summary>
    /// True when the facet's array contains at least one of the candidate values (a non-empty,
    /// non-string enumerable). Order- and duplicate-insensitive.
    /// </summary>
    ArrayContainsAny,

    /// <summary>
    /// True when the facet's array contains every candidate value (a non-empty, non-string
    /// enumerable). Order- and duplicate-insensitive.
    /// </summary>
    ArrayContainsAll,
}
