using System.Text.Json;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;

namespace Aster.AspNetCore;

/// <summary>
/// The JSON body of <c>POST /resources/query</c>: a portable <see cref="ResourceQuery"/> in a wire format that needs
/// no type discriminators. See <c>wiki/AspNetCore-Endpoints.md</c> for examples.
/// </summary>
public sealed record AsterQueryRequest
{
    /// <summary>Gets the definition to restrict results to.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>Gets the version scope: <c>latest</c> (default), <c>allVersions</c>, <c>active</c> or <c>draft</c>.</summary>
    public string? Scope { get; init; }

    /// <summary>Gets the activation channel, required by <c>active</c> scope.</summary>
    public string? Channel { get; init; }

    /// <summary>
    /// Gets the lifecycle filter: <c>active</c> (default; excludes archived and soft-deleted resources), <c>archived</c>,
    /// <c>softDeleted</c> or <c>all</c>.
    /// </summary>
    public string? Lifecycle { get; init; }

    /// <summary>Gets the filter.</summary>
    public AsterFilter? Filter { get; init; }

    /// <summary>Gets the sort order.</summary>
    public IReadOnlyList<AsterSort>? Sort { get; init; }

    /// <summary>Gets the number of results to skip.</summary>
    public int? Skip { get; init; }

    /// <summary>Gets the page size; the endpoint's default page size when omitted.</summary>
    public int? Take { get; init; }
}

/// <summary>
/// One filter node. Set exactly one shape:
/// <list type="bullet">
/// <item><c>and</c>, <c>or</c> or <c>not</c> to combine filters;</item>
/// <item><c>exists</c> with an aspect key for aspect presence;</item>
/// <item><c>field</c>, <c>op</c> and <c>value</c> for a metadata field such as <c>ResourceId</c>;</item>
/// <item><c>aspect</c>, <c>facet</c>, <c>op</c> and <c>value</c> for a facet value.</item>
/// </list>
/// <c>op</c> names a <see cref="ComparisonOperator"/>, case-insensitively (<c>equals</c>, <c>in</c>, <c>range</c>,
/// <c>arrayContainsAny</c>, ...). A <c>range</c> value is an object with <c>min</c>, <c>max</c>, <c>includeMin</c> and
/// <c>includeMax</c>.
/// </summary>
public sealed record AsterFilter
{
    /// <summary>Gets filters that must all match.</summary>
    public IReadOnlyList<AsterFilter>? And { get; init; }

    /// <summary>Gets filters of which at least one must match.</summary>
    public IReadOnlyList<AsterFilter>? Or { get; init; }

    /// <summary>Gets a filter that must not match.</summary>
    public AsterFilter? Not { get; init; }

    /// <summary>Gets an aspect key that must be present.</summary>
    public string? Exists { get; init; }

    /// <summary>Gets the metadata field of a metadata filter.</summary>
    public string? Field { get; init; }

    /// <summary>Gets the aspect key of a facet filter.</summary>
    public string? Aspect { get; init; }

    /// <summary>Gets the facet ID of a facet filter.</summary>
    public string? Facet { get; init; }

    /// <summary>Gets the comparison operator of a metadata or facet filter.</summary>
    public string? Op { get; init; }

    /// <summary>Gets the comparison value of a metadata or facet filter.</summary>
    public JsonElement? Value { get; init; }
}

/// <summary>One sort key of an <see cref="AsterQueryRequest"/>.</summary>
/// <param name="Field">A metadata field, or a facet ID when <paramref name="Aspect"/> is set.</param>
/// <param name="Descending">Whether to sort in descending order.</param>
/// <param name="Aspect">The aspect key of a facet sort.</param>
public sealed record AsterSort(string Field, bool Descending = false, string? Aspect = null);

/// <summary>Translates the query wire format into a <see cref="ResourceQuery"/>.</summary>
internal static class AsterQueryTranslator
{
    public static bool TryTranslate(
        AsterQueryRequest request,
        TenantScope tenant,
        int defaultTake,
        int maxTake,
        out ResourceQuery query,
        out AsterDiagnostic? error)
    {
        query = null!;
        error = null;

        if (!TryParseScope(request.Scope, out var scope))
            return Fail(out error, "query-scope-invalid", "scope", $"Unknown scope '{request.Scope}'.");
        if (!TryParseLifecycle(request.Lifecycle, out var lifecycle))
            return Fail(out error, "query-lifecycle-invalid", "lifecycle", $"Unknown lifecycle '{request.Lifecycle}'.");
        if (request.Skip is < 0 || request.Take is < 0 || request.Take > maxTake)
            return Fail(out error, "query-paging-invalid", "take", $"Skip must not be negative and take must be between 0 and {maxTake}.");

        var sorts = request.Sort ?? [];
        for (var index = 0; index < sorts.Count; index++)
        {
            if (sorts[index] is null || string.IsNullOrWhiteSpace(sorts[index].Field))
                return Fail(out error, "query-sort-invalid", $"sort[{index}].field", "Each sort entry needs a field.");
        }

        FilterExpression? filter = null;
        if (request.Filter is { } wireFilter && !TryTranslate(wireFilter, "filter", depth: 0, out filter, out error))
            return false;

        query = new ResourceQuery
        {
            TenantScope = tenant,
            DefinitionId = request.DefinitionId,
            Scope = scope,
            ActivationChannel = request.Channel,
            LifecycleState = lifecycle,
            Filter = filter,
            Sorts = sorts
                .Select(static sort => new SortExpression(sort.Field, sort.Descending ? SortDirection.Descending : SortDirection.Ascending, sort.Aspect))
                .ToList(),
            Skip = request.Skip,
            Take = request.Take ?? defaultTake,
        };
        return true;
    }

    public static bool TryParseScope(string? value, out ResourceVersionScope scope)
    {
        scope = ResourceVersionScope.Latest;
        return value is null || (Enum.TryParse(value, ignoreCase: true, out scope) && Enum.IsDefined(scope));
    }

    /// <summary>Lifecycle defaults to active resources only (no archive or soft-delete marker); <c>all</c> means no filter.</summary>
    public static bool TryParseLifecycle(string? value, out ResourceLifecycleMarkerState? lifecycle)
    {
        switch (value?.ToLowerInvariant())
        {
            case null or "active":
                lifecycle = ResourceLifecycleMarkerState.None;
                return true;
            case "archived":
                lifecycle = ResourceLifecycleMarkerState.Archived;
                return true;
            case "softdeleted":
                lifecycle = ResourceLifecycleMarkerState.SoftDeleted;
                return true;
            case "all":
                lifecycle = null;
                return true;
            default:
                lifecycle = null;
                return false;
        }
    }

    private const int MaxDepth = 32;

    private static bool TryTranslate(AsterFilter filter, string path, int depth, out FilterExpression? expression, out AsterDiagnostic? error)
    {
        expression = null;
        if (depth > MaxDepth)
            return Fail(out error, "query-filter-too-deep", path, $"Filters may nest at most {MaxDepth} levels.");

        var shapes = (filter.And is not null ? 1 : 0) + (filter.Or is not null ? 1 : 0) + (filter.Not is not null ? 1 : 0)
            + (filter.Exists is not null ? 1 : 0) + (filter.Field is not null ? 1 : 0) + (filter.Aspect is not null || filter.Facet is not null ? 1 : 0);
        if (shapes != 1)
            return Fail(out error, "query-filter-invalid", path, "Set exactly one of and, or, not, exists, field, or aspect with facet.");

        if (filter.And is not null || filter.Or is not null)
        {
            var isAnd = filter.And is not null;
            var operands = new List<FilterExpression>();
            var items = (isAnd ? filter.And : filter.Or)!;
            for (var index = 0; index < items.Count; index++)
            {
                var itemPath = $"{path}.{(isAnd ? "and" : "or")}[{index}]";
                if (items[index] is null)
                    return Fail(out error, "query-filter-invalid", itemPath, "Filter operands must not be null.");
                if (!TryTranslate(items[index], itemPath, depth + 1, out var operand, out error))
                    return false;
                operands.Add(operand!);
            }

            expression = new LogicalExpression(isAnd ? LogicalOperator.And : LogicalOperator.Or, operands);
            error = null;
            return true;
        }

        if (filter.Not is { } negated)
        {
            if (!TryTranslate(negated, $"{path}.not", depth + 1, out var operand, out error))
                return false;
            expression = new LogicalExpression(LogicalOperator.Not, [operand!]);
            return true;
        }

        if (filter.Exists is { } aspectKey)
        {
            expression = new AspectPresenceFilter(aspectKey);
            error = null;
            return true;
        }

        if (!Enum.TryParse<ComparisonOperator>(filter.Op, ignoreCase: true, out var op) || !Enum.IsDefined(op))
            return Fail(out error, "query-operator-invalid", $"{path}.op", $"Unknown operator '{filter.Op}'.");

        object? value = op == ComparisonOperator.Exists && filter.Value is null ? true : ToValue(filter.Value);
        if (value is null)
            return Fail(out error, "query-value-required", $"{path}.value", "A value is required.");

        if (filter.Field is { } field)
        {
            expression = new MetadataFilter(field, value, op);
            error = null;
            return true;
        }

        if (filter.Aspect is null || filter.Facet is null)
            return Fail(out error, "query-filter-invalid", path, "A facet filter needs both aspect and facet.");

        expression = new FacetValueFilter(filter.Aspect, filter.Facet, value, op);
        error = null;
        return true;
    }

    private static object? ToValue(JsonElement? element) => element is { } value ? ToValue(value) : null;

    private static object? ToValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ToValue).OfType<object>().ToList(),
        JsonValueKind.Object => new RangeValue(
            Property(element, "min"),
            Property(element, "max"),
            Flag(element, "includeMin"),
            Flag(element, "includeMax")),
        _ => null,
    };

    private static object? Property(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? ToValue(value) : null;

    private static bool Flag(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.False;

    private static bool Fail(out AsterDiagnostic? error, string code, string path, string message)
    {
        error = new AsterDiagnostic(code, null, null, $"{path}: {message}");
        return false;
    }
}
