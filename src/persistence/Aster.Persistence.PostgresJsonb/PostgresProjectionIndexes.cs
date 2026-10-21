using System.Security.Cryptography;
using System.Text;
using Aster.Core.Models.Infrastructure;
using Aster.Core.Models.Querying;
using Aster.Persistence.PostgresJsonb.Querying;

namespace Aster.Persistence.PostgresJsonb;

/// <summary>
/// One physical index for a declared index projection.
/// </summary>
internal sealed record PostgresProjectionIndex(string Name, string Sql, string Description)
{
    public InfrastructureStep CreateStep => new($"index/create/{Name}", Description);

    public static InfrastructureStep DropStep(string name) => new($"index/drop/{name}", $"Drop {name}: its projection is no longer declared.");
}

/// <summary>
/// Turns declared index projections into indexes on <c>resource_versions</c>, built on exactly the expressions the
/// query translator writes (<see cref="PostgresIndexConjuncts"/>). An index is named after a hash of its definition:
/// the same projection always maps to the same index, and a changed one to a new index that replaces it.
/// </summary>
internal static class PostgresProjectionIndexes
{
    public const string NamePrefix = "ix_aster_projection_";

    public static IReadOnlyList<PostgresProjectionIndex> Plan(IEnumerable<IndexProjection> projections) =>
        projections.Select(Plan).DistinctBy(static index => index.Name).ToList();

    private static PostgresProjectionIndex Plan(IndexProjection projection)
    {
        var (method, key) = projection.Source.Kind == IndexProjectionSourceKind.Metadata
            ? ("btree", MetadataKey(projection))
            : FacetKey(projection);

        // B-tree indexes lead with tenant_id, as every query filters on it; GIN can't without btree_gin.
        var definition = $"ON resource_versions USING {method} ({(method == "gin" ? key : $"tenant_id, {key}")})";
        var name = NamePrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition)))[..16].ToLowerInvariant();
        return new(
            name,
            $"CREATE INDEX IF NOT EXISTS {name} {definition};",
            $"Index '{projection.FieldName}': {projection.Source.Describe()} as {projection.FieldType}.");
    }

    private static (string Method, string Key) FacetKey(IndexProjection projection)
    {
        var value = PostgresFacetValue.Create(Literal, "payload", projection.Source.AspectKey!, projection.Source.FacetKey!);
        return projection.FieldType switch
        {
            IndexFieldType.KeywordArray => ("gin", $"({value.UpperStrings}) jsonb_path_ops"),
            IndexFieldType.Integer or IndexFieldType.Decimal => ("btree", $"({value.IndexedNumber})"),
            IndexFieldType.DateTime => ("btree", $"({value.DateKey}) COLLATE \"C\""),
            _ => ("btree", $"({value.UpperText})"),
        };
    }

    private static string MetadataKey(IndexProjection projection)
    {
        var field = projection.Source.MetadataField!;
        if (PostgresMetadataField.TextColumn(field) is { } column)
            return $"({PostgresText.Upper(column)})";

        return field.ToLowerInvariant() switch
        {
            "version" => "version",
            "created" => "created",
            _ => throw new ArgumentException(
                $"Index projection '{projection.FieldName}' reads metadata field '{field}', which the PostgreSQL JSONB provider can't index.",
                nameof(projection)),
        };
    }

    private static string Literal(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
