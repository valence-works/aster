using Aster.Core.Models.Querying;

namespace Aster.Tests.Persistence;

/// <summary>
/// Index projections over the facets and metadata the query suites filter on, one per index kind.
/// </summary>
public static class PostgresTestProjections
{
    public static IReadOnlyList<IndexProjection> All { get; } =
    [
        IndexProjection.Facet("title", "Title", "Title", IndexFieldType.Text),
        IndexProjection.Facet("category", "Category", "Category", IndexFieldType.Keyword),
        IndexProjection.Facet("amount", "Price", "Amount", IndexFieldType.Decimal),
        IndexProjection.Facet("count", "Inventory", "Count", IndexFieldType.Integer),
        IndexProjection.Facet("startsAt", "Schedule", "StartsAt", IndexFieldType.DateTime),
        IndexProjection.Facet("tags", "Labels", "Tags", IndexFieldType.KeywordArray),
        IndexProjection.Metadata("owner", "Owner", IndexFieldType.Keyword),
    ];
}
