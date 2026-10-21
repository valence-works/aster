using System.Security.Cryptography;
using System.Text;
using Aster.Core.Models.Infrastructure;
using Aster.Core.Models.Querying;
using Aster.Persistence.SqlServerJson.Querying;

namespace Aster.Persistence.SqlServerJson;

internal enum SqlServerProjectionKind
{
    /// <summary>A persisted computed column on <c>resource_versions</c> and an index on it.</summary>
    ComputedColumn,

    /// <summary>An index on an existing <c>resource_versions</c> column.</summary>
    ColumnIndex,

    /// <summary>Rows in <c>aster_projection_elements</c>, one per string element, written by a trigger.</summary>
    ArrayElements,
}

/// <summary>
/// The physical form of one declared index projection. <see cref="Name"/> is derived from a hash of the definition,
/// so the same projection always maps to the same objects and a changed one to new objects that replace them.
/// </summary>
internal sealed record SqlServerProjection(
    string Name,
    string Id,
    SqlServerProjectionKind Kind,
    string? Column,
    IReadOnlyList<string> CreateBatches,
    string Description,
    IndexProjection Source)
{
    public InfrastructureStep CreateStep => new($"index/create/{Name}", Description);

    public static InfrastructureStep DropStep(string name) => new($"index/drop/{name}", $"Drop {name}: its projection is no longer declared.");
}

/// <summary>
/// Turns declared index projections into SQL Server objects, built on expressions the query translator's index
/// conjuncts (<see cref="SqlServerIndexConjuncts"/>) compare with:
/// <list type="bullet">
/// <item>scalar facets and text metadata: a persisted computed column over <c>JSON_VALUE</c> (or the metadata column),
/// indexed with the tenant;</item>
/// <item><c>Version</c> and <c>Created</c>: an index on the column;</item>
/// <item><c>KeywordArray</c> facets: one row per string element in <c>aster_projection_elements</c>, kept by an
/// insert trigger (a computed column can hold one value per row, not one per element).</item>
/// </list>
/// Computed columns read <c>JSON_VALUE</c>, which returns NULL for strings over 4,000 characters; the conjuncts are
/// only added for values short enough that a matching row's value can't be one of those.
/// </summary>
internal static class SqlServerProjections
{
    public const string NamePrefix = "ix_aster_projection_";
    public const string ColumnPrefix = "aster_projection_";
    public const string ElementsTrigger = "aster_projection_elements_on_insert";

    public static IReadOnlyList<SqlServerProjection> Plan(IEnumerable<IndexProjection> projections) =>
        projections.Select(For).DistinctBy(static projection => projection.Name).ToList();

    public static SqlServerProjection For(IndexProjection projection)
    {
        var description = $"Index '{projection.FieldName}': {projection.Source.Describe()} as {projection.FieldType}.";
        if (projection.Source.Kind == IndexProjectionSourceKind.Metadata)
            return ForMetadata(projection, description);

        var aspectKey = projection.Source.AspectKey!;
        var facetKey = projection.Source.FacetKey!;
        if (projection.FieldType == IndexFieldType.KeywordArray)
        {
            var id = Hash($"elements {SqlServerFacetValue.Source(Literal, "payload", aspectKey, facetKey, "facet_0")}");
            return new(
                NamePrefix + id,
                id,
                SqlServerProjectionKind.ArrayElements,
                Column: null,
                [
                    $"""
                    INSERT INTO aster_projection_elements (tenant_id, resource_id, version, projection_id, ordinal, element_key)
                    {ElementsSelect(id, "resource_versions rv", "rv", aspectKey, facetKey)};
                    """,
                    $"INSERT INTO aster_array_projections (projection_id, description) VALUES ({Literal(id)}, {Literal(description)});",
                ],
                description,
                projection);
        }

        var value = FacetJsonValue(aspectKey, facetKey);
        var expression = projection.FieldType switch
        {
            IndexFieldType.Integer or IndexFieldType.Decimal => $"TRY_CONVERT(float, {value})",
            IndexFieldType.DateTime => $"CAST({SqlServerDateKey.Function}({value}) AS nvarchar(28)) COLLATE {SqlServerText.BinaryCollation}",
            // Text: JSON_QUERY renders arrays and objects, as the facet's text does.
            _ => SqlServerText.Key(FacetJsonText(aspectKey, facetKey)),
        };
        return Computed(expression, description, projection);
    }

    /// <summary>
    /// The trigger that writes element rows for every declared <c>KeywordArray</c> projection, or NULL when none is
    /// declared.
    /// </summary>
    public static string? ElementsTriggerSql(IEnumerable<SqlServerProjection> projections)
    {
        var inserts = projections
            .Where(static projection => projection.Kind == SqlServerProjectionKind.ArrayElements)
            .Select(static projection => $"""
                INSERT INTO aster_projection_elements (tenant_id, resource_id, version, projection_id, ordinal, element_key)
                {ElementsSelect(projection.Id, "inserted i", "i", projection.Source.Source.AspectKey!, projection.Source.Source.FacetKey!)};
                """)
            .ToList();
        if (inserts.Count == 0)
            return null;

        return $"""
            CREATE OR ALTER TRIGGER {ElementsTrigger} ON resource_versions AFTER INSERT AS
            BEGIN
                SET NOCOUNT ON;
                {string.Join(Environment.NewLine, inserts)}
            END
            """;
    }

    private static SqlServerProjection ForMetadata(IndexProjection projection, string description)
    {
        var field = projection.Source.MetadataField!;
        if (SqlServerMetadataField.TextColumn(field) is { } column)
            return Computed(SqlServerText.Key(column), description, projection);

        var indexed = field.ToLowerInvariant() switch
        {
            "version" => "version",
            "created" => "created",
            _ => throw new ArgumentException(
                $"Index projection '{projection.FieldName}' reads metadata field '{field}', which the SQL Server JSON provider can't index.",
                nameof(projection)),
        };
        var id = Hash($"column {indexed}");
        return new(
            NamePrefix + id,
            id,
            SqlServerProjectionKind.ColumnIndex,
            Column: null,
            [$"CREATE INDEX {NamePrefix}{id} ON resource_versions (tenant_id, {indexed});"],
            description,
            projection);
    }

    private static SqlServerProjection Computed(string expression, string description, IndexProjection projection)
    {
        var id = Hash($"computed {expression}");
        var column = ColumnPrefix + id;
        return new(
            NamePrefix + id,
            id,
            SqlServerProjectionKind.ComputedColumn,
            column,
            [
                // Separate batches: a batch is compiled whole, before the column exists.
                $"ALTER TABLE resource_versions ADD {column} AS {expression} PERSISTED;",
                $"CREATE INDEX {NamePrefix}{id} ON resource_versions (tenant_id, {column});",
            ],
            description,
            projection);
    }

    // The facet's scalar value, the facet ID's own key first. JSON_VALUE is NULL for a missing key and for JSON null,
    // arrays, objects and strings over 4,000 characters; where the facet ID's key holds any of those, the query's
    // predicate is false (or the conjunct isn't added), so falling through to the camel-case key changes nothing.
    private static string FacetJsonValue(string aspectKey, string facetKey) =>
        Coalesce(FacetPaths(aspectKey, facetKey).Select(static path => $"JSON_VALUE(payload, {path})"));

    private static string FacetJsonText(string aspectKey, string facetKey) =>
        Coalesce(FacetPaths(aspectKey, facetKey).SelectMany(static path => new[] { $"JSON_VALUE(payload, {path})", $"JSON_QUERY(payload, {path})" }));

    private static IEnumerable<string> FacetPaths(string aspectKey, string facetKey) =>
        SqlServerFacetValue.FacetCandidates(facetKey)
            .Select(facet => Literal($"$.aspects.{SqlServerText.JsonPathKey(aspectKey)}.{SqlServerText.JsonPathKey(facet)}"));

    private static string Coalesce(IEnumerable<string> expressions)
    {
        var list = expressions.ToList();
        return list.Count == 1 ? list[0] : $"COALESCE({string.Join(", ", list)})";
    }

    // The facet read exactly as queries read it (SqlServerFacetValue.Source), then one row per string element.
    private static string ElementsSelect(string id, string from, string alias, string aspectKey, string facetKey) => $"""
        SELECT {alias}.tenant_id, {alias}.resource_id, {alias}.version, {Literal(id)}, CAST(element.[key] AS int), {SqlServerText.Key("element.[value]")}
        FROM {from}
        CROSS APPLY {SqlServerFacetValue.Source(Literal, $"{alias}.payload", aspectKey, facetKey, "facet_0")}
        CROSS APPLY OPENJSON(CASE WHEN facet_0.json_type = 4 THEN facet_0.json_value END) element
        WHERE element.[type] = 1
        """;

    private static string Hash(string definition) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(definition)))[..16].ToLowerInvariant();

    public static string Literal(string value) => $"N'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
