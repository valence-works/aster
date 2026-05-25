using System.Collections;
using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Microsoft.Extensions.Logging;

namespace Aster.Core.InMemory;

/// <summary>
/// LINQ-based in-memory implementation of <see cref="IResourceQueryService"/>.
/// Evaluates <see cref="ResourceQuery"/> ASTs against versions supplied by <see cref="IResourceVersionReader"/>.
/// </summary>
/// <remarks>
/// Supported comparators include equality, membership, text, range, and facet existence predicates.
/// </remarks>
public sealed partial class InMemoryQueryService : IResourceQueryService, IResourceQueryProviderIdentity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IResourceVersionReader versionReader;
    private readonly IResourceLifecycleMarkerStore? markerStore;
    private readonly ILogger<InMemoryQueryService> logger;
    private readonly ResourceQueryValidator validator;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryQueryService"/>.
    /// </summary>
    /// <param name="versionReader">The backing resource version reader.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="capabilityProviders">Registered provider capability declarations.</param>
    /// <param name="markerStore">Optional lifecycle marker store for explicit lifecycle-state filtering.</param>
    public InMemoryQueryService(
        IResourceVersionReader versionReader,
        ILogger<InMemoryQueryService> logger,
        IEnumerable<IResourceQueryCapabilitiesProvider>? capabilityProviders = null,
        IResourceLifecycleMarkerStore? markerStore = null)
    {
        ArgumentNullException.ThrowIfNull(versionReader);
        ArgumentNullException.ThrowIfNull(logger);
        this.versionReader = versionReader;
        this.markerStore = markerStore;
        this.logger = logger;
        validator = new ResourceQueryValidator(
            capabilityProviders ?? [new InMemoryQueryCapabilitiesProvider()],
            this);
    }

    /// <inheritdoc />
    public string ProviderKey => InMemoryQueryCapabilitiesProvider.ProviderKey;

    /// <inheritdoc />
    public async ValueTask<IEnumerable<Resource>> QueryAsync(ResourceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(query);

        var candidates = await versionReader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = query.TenantScope,
            Scope = query.Scope,
            ActivationChannel = query.ActivationChannel,
        }, cancellationToken);

        IEnumerable<Resource> result = candidates;

        // Optional DefinitionId shortcut filter
        if (!string.IsNullOrWhiteSpace(query.DefinitionId))
            result = result.Where(r => string.Equals(r.DefinitionId, query.DefinitionId, StringComparison.Ordinal));

        if (query.LifecycleState is not null)
            result = await ApplyLifecycleStateFilterAsync(result, query, cancellationToken);

        // Evaluate the filter expression tree
        if (query.Filter is not null)
            result = result.Where(r => Evaluate(query.Filter, r));

        if (query.Sorts.Count > 0)
            result = result.Order(new ResourceSortComparer(query.Sorts));

        // Pagination
        if (query.Skip.HasValue)
            result = result.Skip(query.Skip.Value);
        if (query.Take.HasValue)
            result = result.Take(query.Take.Value);

        var materialized = result.ToList();
        LogQueryExecuted(query.DefinitionId ?? "(all)", materialized.Count);
        return materialized;
    }

    private async ValueTask<IEnumerable<Resource>> ApplyLifecycleStateFilterAsync(
        IEnumerable<Resource> resources,
        ResourceQuery query,
        CancellationToken cancellationToken)
    {
        if (markerStore is null)
            return resources;

        var materialized = resources.ToList();
        var tenant = TenantScopeResolver.Resolve(query.TenantScope);
        var markers = await markerStore.GetMarkersAsync(
            materialized.Select(static resource => resource.ResourceId).Distinct(StringComparer.Ordinal),
            tenant,
            cancellationToken);
        var expected = query.LifecycleState!.Value;

        return materialized.Where(resource =>
        {
            var actual = markers.TryGetValue(resource.ResourceId, out var marker)
                ? marker.State
                : ResourceLifecycleMarkerState.None;
            return actual == expected;
        });
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
        _                        => throw Unsupported(
            "unsupported-filter-type",
            "predicate",
            $"Filter expression '{expr.GetType().Name}' is not supported by the in-memory query provider.",
            "Filter")
    };

    private static bool EvaluateMetadata(MetadataFilter filter, Resource resource)
    {
        var actual = ResolveMetadataValue(filter.Field, resource);

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
        if (filter.Operator == ComparisonOperator.Exists)
            return facetValue is not null;

        if (facetValue is null)
            return false;

        return ApplyComparator(facetValue, filter.Value, filter.Operator);
    }

    private static string? ResolveFacetValue(object? aspectRaw, string facetDefinitionId)
    {
        if (aspectRaw is null)
            return null;

        if (string.IsNullOrEmpty(facetDefinitionId))
            return null;

        // Case 1: aspect is a Dictionary<string, object> (e.g. stored directly)
        if (aspectRaw is IDictionary<string, object> dict)
        {
            if (dict.TryGetValue(facetDefinitionId, out var val))
                return FormatValueInvariant(val);
            return null;
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
        catch (InvalidOperationException) { /* ignore */ }

        return null;
    }

    private static bool EvaluateLogical(LogicalExpression expr, Resource resource) => expr.Operator switch
    {
        LogicalOperator.And => expr.Operands.All(op => Evaluate(op, resource)),
        LogicalOperator.Or  => expr.Operands.Any(op => Evaluate(op, resource)),
        LogicalOperator.Not => expr.Operands is [var single] && !Evaluate(single, resource),
        _                   => throw Unsupported(
            "unsupported-logical-operator",
            "logical operator",
            $"Logical operator '{expr.Operator}' is not supported by the in-memory query provider.",
            "Filter.Operator")
    };

    private static bool ApplyComparator(object? actual, object? expected, ComparisonOperator op) => op switch
    {
        ComparisonOperator.Equals => ValuesEqual(actual, expected),
        ComparisonOperator.NotEquals => !ValuesEqual(actual, expected),
        ComparisonOperator.In => ApplyInComparator(actual, expected),
        ComparisonOperator.Contains => FormatValueInvariant(actual)?.Contains(
            FormatValueInvariant(expected) ?? string.Empty,
            StringComparison.OrdinalIgnoreCase) == true,
        ComparisonOperator.StartsWith => FormatValueInvariant(actual)?.StartsWith(
            FormatValueInvariant(expected) ?? string.Empty,
            StringComparison.OrdinalIgnoreCase) == true,
        ComparisonOperator.Range => ApplyRangeComparator(actual, expected),
        ComparisonOperator.Exists => actual is not null,
        _ => throw Unsupported(
            "unsupported-comparison-operator",
            "comparison operator",
            $"Comparison operator '{op}' is not supported by the in-memory query provider.",
            "Filter.Operator")
    };

    private static bool ValuesEqual(object? actual, object? expected) =>
        string.Equals(
            FormatValueInvariant(actual),
            FormatValueInvariant(expected),
            StringComparison.OrdinalIgnoreCase);

    private static bool ApplyInComparator(object? actual, object? expected)
    {
        if (expected is string || expected is not IEnumerable enumerable)
            throw Unsupported(
                "in-values-required",
                "value shape",
                "In predicates require a non-string enumerable value set.",
                "Filter.Value");

        var hasElements = false;
        foreach (var candidate in enumerable)
        {
            hasElements = true;
            if (ValuesEqual(actual, candidate))
                return true;
        }

        if (!hasElements)
            throw Unsupported(
                "empty-in-values",
                "value shape",
                "In predicates require at least one candidate value.",
                "Filter.Value");

        return false;
    }

    private static bool ApplyRangeComparator(object? actual, object? expected)
    {
        if (expected is not RangeValue range)
            throw Unsupported(
                "range-value-required",
                "value shape",
                "Range predicates require a RangeValue.",
                "Filter.Value");

        if (actual is null)
            return false;

        if (range.Min is not null)
        {
            var minComparison = CompareValues(actual, range.Min);
            if (range.IncludeMin ? minComparison < 0 : minComparison <= 0)
                return false;
        }

        if (range.Max is not null)
        {
            var maxComparison = CompareValues(actual, range.Max);
            if (range.IncludeMax ? maxComparison > 0 : maxComparison >= 0)
                return false;
        }

        return true;
    }

    private static object? ResolveMetadataValue(string field, Resource resource) => field.ToLowerInvariant() switch
    {
        "resourceid"   => resource.ResourceId,
        "id"           => resource.Id,
        "definitionid" => resource.DefinitionId,
        "owner"        => resource.Owner,
        "version"      => resource.Version,
        "created"      => resource.Created,
        _ => throw Unsupported(
            "unsupported-metadata-field",
            "metadata field",
            $"Metadata field '{field}' is not supported by the in-memory query provider.",
            "Filter.Field")
    };

    private static object? ResolveSortValue(Resource resource, SortExpression sort) =>
        string.IsNullOrWhiteSpace(sort.AspectKey)
            ? ResolveMetadataValue(sort.Field, resource)
            : ResolveFacetValue(resource.Aspects.TryGetValue(sort.AspectKey, out var aspectRaw) ? aspectRaw : null, sort.Field);

    private static int CompareValues(object? left, object? right)
    {
        if (left is null && right is null)
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;

        if (TryConvertDecimal(left, out var leftDecimal) && TryConvertDecimal(right, out var rightDecimal))
            return leftDecimal.CompareTo(rightDecimal);

        if (TryConvertDateTime(left, out var leftDate) && TryConvertDateTime(right, out var rightDate))
            return leftDate.CompareTo(rightDate);

        if (left.GetType() == right.GetType() && left is IComparable comparable)
            return comparable.CompareTo(right);

        return string.Compare(
            FormatValueInvariant(left),
            FormatValueInvariant(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertDecimal(object value, out decimal result)
    {
        if (value is decimal decimalValue)
        {
            result = decimalValue;
            return true;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = convertible.ToDecimal(System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (FormatException) { }
            catch (InvalidCastException) { }
            catch (OverflowException) { }
        }

        result = default;
        return false;
    }

    private static bool TryConvertDateTime(object value, out DateTime result)
    {
        if (value is DateTime dateTime)
        {
            result = dateTime;
            return true;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            result = dateTimeOffset.UtcDateTime;
            return true;
        }

        if (value is string str && DateTime.TryParse(
            str,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            result = parsed;
            return true;
        }

        result = default;
        return false;
    }

    private static string? FormatValueInvariant(object? value) => value switch
    {
        null => null,
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private void ThrowIfInvalid(ResourceQuery query)
    {
        var validation = validator.Validate(query);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);
    }

    private static UnsupportedQueryFeatureException Unsupported(
        string code,
        string feature,
        string message,
        string? path = null) =>
        new(code, feature, message, path);

    private sealed class ResourceSortComparer(IReadOnlyList<SortExpression> sorts) : IComparer<Resource>
    {
        public int Compare(Resource? x, Resource? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return 1;
            if (y is null)
                return -1;

            foreach (var sort in sorts)
            {
                var comparison = CompareValues(ResolveSortValue(x, sort), ResolveSortValue(y, sort));
                if (comparison == 0)
                    continue;

                return sort.Direction == SortDirection.Descending ? -comparison : comparison;
            }

            return 0;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structured log methods (source generated)
    // ──────────────────────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug,
        Message = "Query executed for definition '{DefinitionId}', returned {Count} result(s).")]
    private partial void LogQueryExecuted(string definitionId, int count);
}
