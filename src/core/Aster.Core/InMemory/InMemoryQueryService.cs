using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Microsoft.Extensions.Logging;

namespace Aster.Core.InMemory;

/// <summary>
/// LINQ-based in-memory implementation of <see cref="IResourceQueryService"/>.
/// Evaluates <see cref="ResourceQuery"/> ASTs against the latest version of each resource.
/// </summary>
/// <remarks>
/// Supported comparators: <see cref="ComparisonOperator.Equals"/> and <see cref="ComparisonOperator.Contains"/>.
/// <see cref="ComparisonOperator.Range"/> throws <see cref="NotSupportedException"/> (spec §6, Phase 1 scope).
/// </remarks>
public sealed class InMemoryQueryService : IResourceQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly InMemoryResourceStore store;
    private readonly ILogger<InMemoryQueryService> logger;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryQueryService"/>.
    /// </summary>
    /// <param name="store">The backing in-memory resource store.</param>
    /// <param name="logger">The logger.</param>
    public InMemoryQueryService(InMemoryResourceStore store, ILogger<InMemoryQueryService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> QueryAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Validate the filter AST upfront — Range is unsupported (spec §6)
        if (query.Filter is not null)
            ValidateFilterExpression(query.Filter);

        // Collect latest version of every resource
        var candidates = new List<Resource>();
        foreach (var versionList in store.Versions.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Resource? latest;
            lock (versionList)
            {
                latest = versionList.Count > 0 ? versionList[^1] : null;
            }

            if (latest is not null)
                candidates.Add(latest);
        }

        IEnumerable<Resource> result = candidates;

        // Optional DefinitionId shortcut filter
        if (!string.IsNullOrWhiteSpace(query.DefinitionId))
            result = result.Where(r => string.Equals(r.DefinitionId, query.DefinitionId, StringComparison.Ordinal));

        // Evaluate the filter expression tree
        if (query.Filter is not null)
            result = result.Where(r => Evaluate(query.Filter, r));

        // Pagination
        if (query.Skip.HasValue)
            result = result.Skip(query.Skip.Value);
        if (query.Take.HasValue)
            result = result.Take(query.Take.Value);

        return ValueTask.FromResult(result.ToList().AsEnumerable());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Upfront AST validation
    // ──────────────────────────────────────────────────────────────────────────

    private static void ValidateFilterExpression(FilterExpression expr)
    {
        switch (expr)
        {
            case MetadataFilter m when m.Operator == ComparisonOperator.Range:
            case FacetValueFilter fv when fv.Operator == ComparisonOperator.Range:
                throw new NotSupportedException("Range comparator is not supported by the Phase 1 in-memory evaluator (spec §6).");
            case LogicalExpression logic:
                foreach (var operand in logic.Operands)
                    ValidateFilterExpression(operand);
                break;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // AST evaluator
    // ──────────────────────────────────────────────────────────────────────────

    private static bool Evaluate(FilterExpression expr, Resource resource) => expr switch
    {
        MetadataFilter meta      => EvaluateMetadata(meta, resource),
        AspectPresenceFilter asp => EvaluateAspectPresence(asp, resource),
        FacetValueFilter fv      => EvaluateFacetValue(fv, resource),
        LogicalExpression logic  => EvaluateLogical(logic, resource),
        _                        => throw new NotSupportedException($"Unknown filter expression type: {expr.GetType().Name}")
    };

    private static bool EvaluateMetadata(MetadataFilter filter, Resource resource)
    {
        var actual = filter.Field.ToLowerInvariant() switch
        {
            "resourceid"   => resource.ResourceId,
            "definitionid" => resource.DefinitionId,
            "owner"        => resource.Owner ?? string.Empty,
            "version"      => resource.Version.ToString(),
            _ => throw new NotSupportedException($"Metadata field '{filter.Field}' is not supported by the in-memory evaluator.")
        };

        return ApplyComparator(actual, filter.Value, filter.Operator);
    }

    private static bool EvaluateAspectPresence(AspectPresenceFilter filter, Resource resource) =>
        resource.Aspects.ContainsKey(filter.AspectKey);

    private static bool EvaluateFacetValue(FacetValueFilter filter, Resource resource)
    {
        if (!resource.Aspects.TryGetValue(filter.AspectKey, out var aspectRaw))
            return false;

        // Resolve the facet value from the aspect payload
        var facetValue = ResolveFacetValue(aspectRaw, filter.FacetDefinitionId);
        if (facetValue is null)
            return false;

        return ApplyComparator(facetValue, filter.Value?.ToString() ?? string.Empty, filter.Operator);
    }

    private static string? ResolveFacetValue(object aspectRaw, string facetDefinitionId)
    {
        // Case 1: aspect is a Dictionary<string, object> (e.g. stored directly)
        if (aspectRaw is IDictionary<string, object> dict)
        {
            return dict.TryGetValue(facetDefinitionId, out var val) ? val?.ToString() : null;
        }

        // Case 2: aspect is a JSON string → parse it
        if (aspectRaw is string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty(facetDefinitionId, out var elem))
                    return elem.ToString();
                // Try camelCase fallback for System.Text.Json web defaults
                var camel = char.ToLowerInvariant(facetDefinitionId[0]) + facetDefinitionId[1..];
                if (doc.RootElement.TryGetProperty(camel, out var elemCamel))
                    return elemCamel.ToString();
            }
            catch (JsonException)
            {
                // Not JSON — treat as scalar
                return json;
            }
            return null;
        }

        // Case 3: aspect is a JsonElement
        if (aspectRaw is JsonElement element)
        {
            if (element.TryGetProperty(facetDefinitionId, out var prop))
                return prop.ToString();
            var camel = char.ToLowerInvariant(facetDefinitionId[0]) + facetDefinitionId[1..];
            if (element.TryGetProperty(camel, out var propCamel))
                return propCamel.ToString();
            return null;
        }

        // Case 4: aspect is a POCO — round-trip to JSON
        try
        {
            var jsonStr = JsonSerializer.Serialize(aspectRaw, JsonOptions);
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.TryGetProperty(facetDefinitionId, out var elem))
                return elem.ToString();
            var camel = char.ToLowerInvariant(facetDefinitionId[0]) + facetDefinitionId[1..];
            if (doc.RootElement.TryGetProperty(camel, out var elemCamel))
                return elemCamel.ToString();
        }
        catch (JsonException) { /* ignore */ }

        return null;
    }

    private static bool EvaluateLogical(LogicalExpression expr, Resource resource) => expr.Operator switch
    {
        LogicalOperator.And => expr.Operands.All(op => Evaluate(op, resource)),
        LogicalOperator.Or  => expr.Operands.Any(op => Evaluate(op, resource)),
        LogicalOperator.Not => expr.Operands is [var single] && !Evaluate(single, resource),
        _                   => throw new NotSupportedException($"Unsupported logical operator: {expr.Operator}")
    };

    private static bool ApplyComparator(string actual, string expected, ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equals   => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.Contains => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
        ComparisonOperator.Range    => throw new NotSupportedException("Range comparator is not supported by the Phase 1 in-memory evaluator (spec §6)."),
        _                           => throw new NotSupportedException($"Unknown comparator: {op}")
    };
}
