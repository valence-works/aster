using System.Text.Json;
using Aster.Core.Extensions;
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
/// <item><c>aspect</c>, <c>facet</c>, <c>op</c> and <c>value</c> for a facet value;</item>
/// <item><c>aspect</c>, <c>facet</c> and <c>references</c> for a reference facet that names a resource: a
/// <c>reference</c> facet equal to it or a <c>reference[]</c> facet containing it.</item>
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

    /// <summary>Gets the <c>ResourceId</c> a reference filter's facet must name.</summary>
    public string? References { get; init; }
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
        out AsterQueryPaths paths,
        out AsterDiagnostic? error)
    {
        query = null!;
        paths = new AsterQueryPaths();
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
        if (request.Filter is { } wireFilter && !TryTranslate(wireFilter, "Filter", "filter", depth: 0, paths, out filter, out error))
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

    // corePath names the node as the query validator does (Filter.Operands[0]); path names it in the wire format.
    private static bool TryTranslate(
        AsterFilter filter,
        string corePath,
        string path,
        int depth,
        AsterQueryPaths paths,
        out FilterExpression? expression,
        out AsterDiagnostic? error)
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
            var key = filter.And is not null ? "and" : "or";
            paths.Add(corePath, path, key);
            var operands = new List<FilterExpression>();
            var items = (filter.And ?? filter.Or)!;
            for (var index = 0; index < items.Count; index++)
            {
                var itemPath = $"{path}.{key}[{index}]";
                if (items[index] is null)
                    return Fail(out error, "query-filter-invalid", itemPath, "Filter operands must not be null.");
                if (!TryTranslate(items[index], $"{corePath}.Operands[{index}]", itemPath, depth + 1, paths, out var operand, out error))
                    return false;
                operands.Add(operand!);
            }

            expression = new LogicalExpression(key == "and" ? LogicalOperator.And : LogicalOperator.Or, operands);
            error = null;
            return true;
        }

        if (filter.Not is { } negated)
        {
            paths.Add(corePath, path, "not");
            if (!TryTranslate(negated, $"{corePath}.Operands[0]", $"{path}.not", depth + 1, paths, out var operand, out error))
                return false;
            expression = new LogicalExpression(LogicalOperator.Not, [operand!]);
            return true;
        }

        paths.Add(corePath, path, operandsKey: null);
        if (filter.Exists is { } aspectKey)
        {
            expression = new AspectPresenceFilter(aspectKey);
            error = null;
            return true;
        }

        if (filter.References is { } target)
        {
            if (filter.Aspect is null || filter.Facet is null || filter.Op is not null || filter.Value is not null)
                return Fail(out error, "query-filter-invalid", path, "A reference filter needs aspect, facet and references, and no op or value.");
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(filter.Aspect) || string.IsNullOrWhiteSpace(filter.Facet))
                return Fail(out error, "query-value-required", $"{path}.references", "A reference filter needs a non-empty aspect, facet and references.");

            // Translated to an Or of two facet filters; the validator's paths into it all name this node.
            paths.MarkOpaque(corePath, valueKey: "references");
            expression = ReferenceQuery.Targets(filter.Aspect, filter.Facet, target);
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
        error = new AsterDiagnostic(code, null, null, message) { Path = path };
        return false;
    }
}

/// <summary>
/// Maps the paths <see cref="Core.Abstractions.IResourceQueryValidator"/> reports, which name the translated
/// <see cref="ResourceQuery"/> (<c>Filter.Operands[1].Operator</c>, <c>Sorts[0].Field</c>), onto the request body's
/// (<c>filter.and[1].op</c>, <c>sort[0].field</c>), so a client can place a failure on what it sent.
/// </summary>
internal sealed class AsterQueryPaths
{
    private static readonly Dictionary<string, string> Members = new(StringComparer.Ordinal)
    {
        ["DefinitionId"] = "definitionId",
        ["Scope"] = "scope",
        ["ActivationChannel"] = "channel",
        ["LifecycleState"] = "lifecycle",
        ["Skip"] = "skip",
        ["Take"] = "take",
        ["Sorts"] = "sort",
    };

    // Segments inside a filter or sort node; any other segment is dropped, leaving the node's own path.
    private static readonly Dictionary<string, string> NodeMembers = new(StringComparer.Ordinal)
    {
        ["Field"] = "field",
        ["Operator"] = "op",
        ["Value"] = "value",
        ["Direction"] = "descending",
        ["AspectKey"] = "aspect",
        ["FacetDefinitionId"] = "facet",
    };

    private readonly Dictionary<string, Node> nodes = new(StringComparer.Ordinal);

    public void Add(string corePath, string path, string? operandsKey) => nodes[corePath] = new Node(path, operandsKey, ValueKey: null);

    public void MarkOpaque(string corePath, string valueKey) => nodes[corePath] = nodes[corePath] with { ValueKey = valueKey, Opaque = true };

    /// <summary>The request-body path for a validator path; <see langword="null"/> for one the body has no member for, such as <c>TenantScope</c>.</summary>
    public string? ToRequestPath(string? corePath)
    {
        if (corePath is null)
            return null;

        if (corePath == "Filter" || corePath.StartsWith("Filter.", StringComparison.Ordinal) || corePath.StartsWith("Filter[", StringComparison.Ordinal))
            return FilterPath(corePath);

        var end = corePath.IndexOfAny(['.', '[']);
        var head = end < 0 ? corePath : corePath[..end];
        if (!Members.TryGetValue(head, out var member))
            return null;

        // Sorts[0].Field → sort[0].field
        var rest = end < 0 ? string.Empty : corePath[end..];
        var index = rest.StartsWith('[') ? rest[..(rest.IndexOf(']') + 1)] : string.Empty;
        return member + index + NodeMember(rest[index.Length..]);
    }

    private string FilterPath(string corePath)
    {
        // The longest translated node that contains the path: Filter.Operands[1].Operator is in Filter.Operands[1].
        var prefix = corePath;
        Node? node;
        while (!nodes.TryGetValue(prefix, out node))
        {
            var cut = Math.Max(prefix.LastIndexOf('.'), prefix.LastIndexOf('['));
            if (cut <= 0)
                return "filter";
            prefix = prefix[..cut];
        }

        var rest = corePath[prefix.Length..];
        if (node.Opaque)
            return rest.Contains("Value", StringComparison.Ordinal) ? $"{node.Path}.{node.ValueKey}" : node.Path;
        if (node.OperandsKey is { } key)
            return rest.StartsWith(".Operands", StringComparison.Ordinal) ? $"{node.Path}.{key}" : node.Path;

        return node.Path + NodeMember(rest);
    }

    // ".Value.Min" → ".value.min", ".Value[2]" → ".value[2]", ".Operator" → ".op".
    private static string NodeMember(string rest)
    {
        if (rest.Length == 0 || rest[0] != '.')
            return string.Empty;

        var end = rest.IndexOfAny(['.', '['], 1);
        var segment = end < 0 ? rest[1..] : rest[1..end];
        if (!NodeMembers.TryGetValue(segment, out var member))
            return string.Empty;

        var tail = end < 0 ? string.Empty : rest[end..];
        return $".{member}{(segment == "Value" ? CamelCase(tail) : string.Empty)}";
    }

    private static string CamelCase(string tail) =>
        string.Concat(tail.Split('.').Select(static (part, index) =>
            index == 0 || part.Length == 0 ? part : "." + char.ToLowerInvariant(part[0]) + part[1..]));

    private sealed record Node(string Path, string? OperandsKey, string? ValueKey, bool Opaque = false);
}
