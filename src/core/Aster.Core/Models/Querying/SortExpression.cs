namespace Aster.Core.Models.Querying;

/// <summary>
/// Sort direction for query results.
/// </summary>
public enum SortDirection
{
    /// <summary>Smallest/oldest/A-Z first.</summary>
    Ascending,

    /// <summary>Largest/newest/Z-A first.</summary>
    Descending,
}

/// <summary>
/// Sorts query results by a metadata field or by a facet value when <see cref="AspectKey"/> is supplied.
/// </summary>
/// <param name="Field">Metadata field name, or facet definition id when <paramref name="AspectKey"/> is supplied.</param>
/// <param name="Direction">The sort direction.</param>
/// <param name="AspectKey">Optional aspect key for facet-value sorting.</param>
public sealed record SortExpression(
    string Field,
    SortDirection Direction = SortDirection.Ascending,
    string? AspectKey = null);
