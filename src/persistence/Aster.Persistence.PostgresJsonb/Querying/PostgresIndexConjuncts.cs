using Aster.Core.Models.Querying;
using Aster.Persistence.Relational.Querying;
using static Aster.Persistence.Relational.Querying.RelationalQueryValues;

namespace Aster.Persistence.PostgresJsonb.Querying;

/// <summary>
/// For a predicate over a declared index projection, a conjunct written in exactly the indexed expression
/// (<see cref="PostgresProjectionIndexes"/>), so the planner can use the index. Each conjunct is implied by the
/// predicate it accompanies: it narrows the rows the planner reads, never the rows the query returns.
/// </summary>
internal sealed class PostgresIndexConjuncts(PostgresParameterBag parameters, IReadOnlyList<IndexProjection> projections)
{
    public string? For(FilterExpression filter) => filter switch
    {
        FacetValueFilter facet when FindFacet(facet) is { } projection => ForFacet(facet, projection),
        MetadataFilter metadata when FindMetadata(metadata) is not null => ForMetadata(metadata),
        _ => null,
    };

    private IndexProjection? FindFacet(FacetValueFilter filter) => projections.FirstOrDefault(projection =>
        projection.Source.Kind == IndexProjectionSourceKind.Facet
        && string.Equals(projection.Source.AspectKey, filter.AspectKey, StringComparison.Ordinal)
        && string.Equals(projection.Source.FacetKey, filter.FacetDefinitionId, StringComparison.Ordinal));

    private IndexProjection? FindMetadata(MetadataFilter filter) => projections.FirstOrDefault(projection =>
        projection.Source.Kind == IndexProjectionSourceKind.Metadata
        && string.Equals(projection.Source.MetadataField, filter.Field, StringComparison.OrdinalIgnoreCase)
        && PostgresMetadataField.TextColumn(filter.Field) is not null);

    private string? ForFacet(FacetValueFilter filter, IndexProjection projection)
    {
        var value = PostgresFacetValue.Create(parameters, filter.AspectKey, filter.FacetDefinitionId);
        return (projection.FieldType, filter.Operator) switch
        {
            (IndexFieldType.KeywordArray, ComparisonOperator.ArrayContains) => ArrayContains(value, [filter.Value], requireAll: true),
            (IndexFieldType.KeywordArray, ComparisonOperator.ArrayContainsAll) => ArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: true),
            (IndexFieldType.KeywordArray, ComparisonOperator.ArrayContainsAny) => ArrayContains(value, ResolveArrayCandidates(filter.Value), requireAll: false),
            (IndexFieldType.KeywordArray, _) => null,
            (IndexFieldType.Integer or IndexFieldType.Decimal, ComparisonOperator.Equals) => Numbers(value.IndexedNumber, [filter.Value]),
            (IndexFieldType.Integer or IndexFieldType.Decimal, ComparisonOperator.In) => Numbers(value.IndexedNumber, ResolveInValues(filter.Value)),
            (IndexFieldType.Integer or IndexFieldType.Decimal, ComparisonOperator.Range) => NumericRange(value, RequireRange(filter.Value)),
            (IndexFieldType.DateTime, ComparisonOperator.Range) => DateTimeRange(value, RequireRange(filter.Value)),
            // Text projections serve the text path only: a numeric-looking value is compared as a number instead.
            (_, ComparisonOperator.Equals) => Texts(value.UpperText, [filter.Value], allowNumeric: false),
            (_, ComparisonOperator.In) => Texts(value.UpperText, ResolveInValues(filter.Value), allowNumeric: false),
            _ => null,
        };
    }

    private string? ForMetadata(MetadataFilter filter)
    {
        var column = PostgresText.Upper($"rv.{PostgresMetadataField.TextColumn(filter.Field)}");
        return filter.Operator switch
        {
            ComparisonOperator.Equals => Texts(column, [filter.Value], allowNumeric: true),
            ComparisonOperator.In => Texts(column, ResolveInValues(filter.Value), allowNumeric: true),
            _ => null,
        };
    }

    private string? Texts(string indexed, IReadOnlyList<object?> values, bool allowNumeric)
    {
        if (values.Any(value => FormatValue(value) is null || (!allowNumeric && TryConvertDouble(value, out _))))
            return null;

        return $"{indexed} IN ({string.Join(", ", values.Select(value => PostgresText.Upper(parameters.Add(FormatValue(value)!))))})";
    }

    private string? Numbers(string indexed, IReadOnlyList<object?> values)
    {
        var numbers = new List<double>();
        foreach (var value in values)
        {
            if (!TryConvertDouble(value, out var number))
                return null;

            numbers.Add(number);
        }

        return $"{indexed} IN ({string.Join(", ", numbers.Select(number => parameters.Add(number)))})";
    }

    private string? NumericRange(PostgresFacetValue value, RangeValue range) =>
        ResolveRangeKind(range) == RelationalRangeKind.Numeric
            ? Bounds(value.IndexedNumber, range, bound => ConvertToDouble(bound, "range bound"))
            : null;

    private string? DateTimeRange(PostgresFacetValue value, RangeValue range) =>
        ResolveRangeKind(range) == RelationalRangeKind.DateTime
            ? Bounds($"{value.DateKey} COLLATE \"C\"", range, bound => ConvertToDateKey(bound, "range bound", "Filter.Value"))
            : null;

    private string Bounds(string indexed, RangeValue range, Func<object, object> convert)
    {
        var bounds = new List<string>();
        if (range.Min is not null)
            bounds.Add($"{indexed} {(range.IncludeMin ? ">=" : ">")} {parameters.Add(convert(range.Min))}");

        if (range.Max is not null)
            bounds.Add($"{indexed} {(range.IncludeMax ? "<=" : "<")} {parameters.Add(convert(range.Max))}");

        return string.Join(" AND ", bounds);
    }

    // Only text candidates can use the index of upper-cased strings; others keep the per-element match alone.
    private string? ArrayContains(PostgresFacetValue value, IReadOnlyList<object?> candidates, bool requireAll)
    {
        if (candidates.Any(candidate => !QueryArrayValue.TryClassify(candidate, out var kind) || kind != QueryArrayElementKind.Text))
            return null;

        var texts = candidates.Select(candidate => PostgresText.Upper(parameters.Add(QueryArrayValue.FormatText(candidate!)))).ToList();
        return requireAll
            ? $"{value.UpperStrings} @> jsonb_build_array({string.Join(", ", texts)})"
            : string.Join(" OR ", texts.Select(text => $"{value.UpperStrings} @> jsonb_build_array({text})"));
    }
}
