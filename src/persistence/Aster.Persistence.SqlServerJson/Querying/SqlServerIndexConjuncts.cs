using Aster.Core.Models.Querying;
using Aster.Persistence.Relational.Querying;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.SqlServerJson.Querying;

/// <summary>
/// For a predicate over a declared index projection, a conjunct over exactly the indexed column or element rows
/// (<see cref="SqlServerProjections"/>), so the optimizer can seek the index. Each conjunct is implied by the
/// predicate it accompanies: it narrows the rows the server reads, never the rows the query returns.
/// </summary>
internal sealed class SqlServerIndexConjuncts(SqlServerParameterBag parameters, IReadOnlyList<IndexProjection> projections)
{
    // JSON_VALUE, which computed columns read, returns NULL for longer strings.
    private const int MaxJsonValueLength = 4000;

    public string? For(FilterExpression filter) => filter switch
    {
        FacetValueFilter facet when FindFacet(facet) is { } projection => ForFacet(facet, SqlServerProjections.For(projection)),
        MetadataFilter metadata when FindMetadata(metadata) is { } projection => ForMetadata(metadata, SqlServerProjections.For(projection)),
        _ => null,
    };

    private IndexProjection? FindFacet(FacetValueFilter filter) => projections.FirstOrDefault(projection =>
        projection.Source.Kind == IndexProjectionSourceKind.Facet
        && string.Equals(projection.Source.AspectKey, filter.AspectKey, StringComparison.Ordinal)
        && string.Equals(projection.Source.FacetKey, filter.FacetDefinitionId, StringComparison.Ordinal));

    private IndexProjection? FindMetadata(MetadataFilter filter) => projections.FirstOrDefault(projection =>
        projection.Source.Kind == IndexProjectionSourceKind.Metadata
        && string.Equals(projection.Source.MetadataField, filter.Field, StringComparison.OrdinalIgnoreCase)
        && SqlServerMetadataField.TextColumn(filter.Field) is not null);

    private string? ForFacet(FacetValueFilter filter, SqlServerProjection projection)
    {
        if (projection.Kind == SqlServerProjectionKind.ArrayElements)
        {
            return filter.Operator switch
            {
                ComparisonOperator.ArrayContains => Elements(projection, [filter.Value], requireAll: true),
                ComparisonOperator.ArrayContainsAll => Elements(projection, ResolveArrayCandidates(filter.Value), requireAll: true),
                ComparisonOperator.ArrayContainsAny => Elements(projection, ResolveArrayCandidates(filter.Value), requireAll: false),
                _ => null,
            };
        }

        var column = $"rv.{projection.Column}";
        return (projection.Source.FieldType, filter.Operator) switch
        {
            (IndexFieldType.Integer or IndexFieldType.Decimal, ComparisonOperator.Equals) => Numbers(column, [filter.Value]),
            (IndexFieldType.Integer or IndexFieldType.Decimal, ComparisonOperator.In) => Numbers(column, ResolveInValues(filter.Value)),
            (IndexFieldType.Integer or IndexFieldType.Decimal, ComparisonOperator.Range) => NumericRange(column, RequireRange(filter.Value)),
            (IndexFieldType.DateTime, ComparisonOperator.Range) => DateTimeRange(column, RequireRange(filter.Value)),
            (IndexFieldType.Integer or IndexFieldType.Decimal or IndexFieldType.DateTime, _) => null,
            // Text projections serve the text path only: a numeric-looking value is compared as a number instead.
            (_, ComparisonOperator.Equals) => Texts(column, [filter.Value], allowNumeric: false),
            (_, ComparisonOperator.In) => Texts(column, ResolveInValues(filter.Value), allowNumeric: false),
            _ => null,
        };
    }

    private string? ForMetadata(MetadataFilter filter, SqlServerProjection projection) => filter.Operator switch
    {
        ComparisonOperator.Equals => Texts($"rv.{projection.Column}", [filter.Value], allowNumeric: true),
        ComparisonOperator.In => Texts($"rv.{projection.Column}", ResolveInValues(filter.Value), allowNumeric: true),
        _ => null,
    };

    private string? Texts(string column, IReadOnlyList<object?> values, bool allowNumeric)
    {
        if (values.Any(value => FormatValue(value) is not { Length: <= MaxJsonValueLength } || (!allowNumeric && TryConvertDouble(value, out _))))
            return null;

        return $"{column} IN ({string.Join(", ", values.Select(value => SqlServerText.Key(parameters.Add(FormatValue(value)!))))})";
    }

    private string? Numbers(string column, IReadOnlyList<object?> values)
    {
        var numbers = new List<double>();
        foreach (var value in values)
        {
            if (!TryConvertDouble(value, out var number))
                return null;

            numbers.Add(number);
        }

        return $"{column} IN ({string.Join(", ", numbers.Select(number => parameters.Add(number)))})";
    }

    private string? NumericRange(string column, RangeValue range) =>
        ResolveRangeKind(range) == RelationalRangeKind.Numeric
            ? Bounds(column, range, bound => ConvertToDouble(bound, "range bound"))
            : null;

    private string? DateTimeRange(string column, RangeValue range) =>
        ResolveRangeKind(range) == RelationalRangeKind.DateTime
            ? Bounds(column, range, bound => ConvertToDateKey(bound, "range bound", "Filter.Value"))
            : null;

    private string Bounds(string column, RangeValue range, Func<object, object> convert)
    {
        var bounds = new List<string>();
        if (range.Min is not null)
            bounds.Add($"{column} {(range.IncludeMin ? ">=" : ">")} {parameters.Add(convert(range.Min))}");

        if (range.Max is not null)
            bounds.Add($"{column} {(range.IncludeMax ? "<=" : "<")} {parameters.Add(convert(range.Max))}");

        return string.Join(" AND ", bounds);
    }

    // Only text candidates have element rows; others keep the per-element match alone.
    private string? Elements(SqlServerProjection projection, IReadOnlyList<object?> candidates, bool requireAll)
    {
        if (candidates.Any(candidate => !QueryArrayValue.TryClassify(candidate, out var kind) || kind != QueryArrayElementKind.Text))
            return null;

        var matches = candidates
            .Select(candidate => $"""
                EXISTS (SELECT 1 FROM aster_projection_elements projection_element
                    WHERE projection_element.projection_id = {SqlServerProjections.Literal(projection.Id)}
                        AND projection_element.tenant_id = rv.tenant_id
                        AND projection_element.element_key = {SqlServerText.Key(parameters.Add(QueryArrayValue.FormatText(candidate!)))}
                        AND projection_element.resource_id = rv.resource_id
                        AND projection_element.version = rv.version)
                """)
            .ToList();
        return string.Join(requireAll ? " AND " : " OR ", matches.Select(static match => $"({match})"));
    }
}
