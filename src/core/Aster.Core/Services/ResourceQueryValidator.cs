using System.Globalization;
using Aster.Core.Abstractions;
using Aster.Core.Models.Querying;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IResourceQueryValidator"/> implementation.
/// </summary>
public sealed class ResourceQueryValidator : IResourceQueryValidator
{
    private readonly QueryCapabilityDescription? capabilities;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceQueryValidator"/> class.
    /// </summary>
    /// <param name="capabilityProviders">Registered provider capability declarations.</param>
    public ResourceQueryValidator(IEnumerable<IResourceQueryCapabilitiesProvider> capabilityProviders)
    {
        ArgumentNullException.ThrowIfNull(capabilityProviders);
        capabilities = capabilityProviders.LastOrDefault()?.Capabilities;
    }

    /// <inheritdoc />
    public QueryValidationResult Validate(ResourceQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (capabilities is null)
        {
            return new([
                Failure(
                    "capabilities-not-declared",
                    "Query capabilities are not declared for the active provider.",
                    feature: "capabilities missing"),
            ]);
        }

        var failures = new List<QueryValidationFailure>();
        ValidateScope(query, failures);
        ValidatePaging(query, failures);

        if (query.Filter is not null)
            ValidateFilter(query.Filter, "Filter", failures);

        ValidateSorts(query.Sorts, failures);

        return failures.Count == 0
            ? QueryValidationResult.Success
            : new(failures);
    }

    private void ValidateScope(ResourceQuery query, List<QueryValidationFailure> failures)
    {
        var providerCapabilities = capabilities!;

        if (!Enum.IsDefined(query.Scope) || !providerCapabilities.SupportedScopes.Contains(query.Scope))
        {
            failures.Add(Failure(
                "unsupported-scope",
                $"Scope '{query.Scope}' is not supported by {providerCapabilities.ProviderName}.",
                "Scope",
                "scope"));
        }

        if (query.Scope == ResourceVersionScope.Active
            && providerCapabilities.RequiresActivationChannelForActiveScope
            && string.IsNullOrWhiteSpace(query.ActivationChannel))
        {
            failures.Add(Failure(
                "activation-channel-required",
                $"Active scope requires an activation channel for {providerCapabilities.ProviderName}.",
                "ActivationChannel",
                "scope"));
        }
    }

    private void ValidatePaging(ResourceQuery query, List<QueryValidationFailure> failures)
    {
        if (query.Skip is < 0)
        {
            failures.Add(Failure(
                "negative-skip",
                "Skip must be zero or greater.",
                "Skip",
                "paging"));
        }
        else if (query.Skip.HasValue && !capabilities!.SupportsSkip)
        {
            failures.Add(Failure(
                "unsupported-skip",
                $"Skip paging is not supported by {capabilities.ProviderName}.",
                "Skip",
                "paging"));
        }

        if (query.Take is < 0)
        {
            failures.Add(Failure(
                "negative-take",
                "Take must be zero or greater.",
                "Take",
                "paging"));
        }
        else if (query.Take.HasValue && !capabilities!.SupportsTake)
        {
            failures.Add(Failure(
                "unsupported-take",
                $"Take paging is not supported by {capabilities.ProviderName}.",
                "Take",
                "paging"));
        }
    }

    private void ValidateFilter(FilterExpression expression, string path, List<QueryValidationFailure> failures)
    {
        switch (expression)
        {
            case MetadataFilter metadata:
                ValidateMetadataFilter(metadata, path, failures);
                break;
            case AspectPresenceFilter:
                ValidateFilterType(QueryFilterType.AspectPresence, path, failures);
                break;
            case FacetValueFilter facet:
                ValidateFacetValueFilter(facet, path, failures);
                break;
            case LogicalExpression logical:
                ValidateLogicalExpression(logical, path, failures);
                break;
            default:
                failures.Add(Failure(
                    "unsupported-filter-type",
                    $"Filter expression '{expression.GetType().Name}' is not supported by {capabilities!.ProviderName}.",
                    path,
                    "predicate"));
                break;
        }
    }

    private void ValidateMetadataFilter(MetadataFilter filter, string path, List<QueryValidationFailure> failures)
    {
        if (!ValidateFilterType(QueryFilterType.Metadata, path, failures))
            return;

        if (!capabilities!.SupportedMetadataFields.Contains(filter.Field))
        {
            failures.Add(Failure(
                "unsupported-metadata-field",
                $"Metadata field '{filter.Field}' is not supported by {capabilities.ProviderName}.",
                $"{path}.Field",
                "metadata field"));
        }

        ValidateComparison(QueryFilterType.Metadata, filter.Operator, $"{path}.Operator", failures);
    }

    private void ValidateFacetValueFilter(FacetValueFilter filter, string path, List<QueryValidationFailure> failures)
    {
        if (!ValidateFilterType(QueryFilterType.FacetValue, path, failures))
            return;

        ValidateComparison(QueryFilterType.FacetValue, filter.Operator, $"{path}.Operator", failures);

        if (filter.Operator == ComparisonOperator.Range)
            ValidateRangeValue(filter.Value, $"{path}.Value", failures);
    }

    private void ValidateLogicalExpression(LogicalExpression expression, string path, List<QueryValidationFailure> failures)
    {
        if (!ValidateFilterType(QueryFilterType.Logical, path, failures))
            return;

        if (!capabilities!.SupportedLogicalOperators.Contains(expression.Operator))
        {
            failures.Add(Failure(
                "unsupported-logical-operator",
                $"Logical operator '{expression.Operator}' is not supported by {capabilities.ProviderName}.",
                $"{path}.Operator",
                "logical operator"));
        }

        if (expression.Operator == LogicalOperator.Not && expression.Operands.Count != 1)
        {
            failures.Add(Failure(
                "invalid-not-operands",
                "NOT logical expressions require exactly one operand.",
                $"{path}.Operands",
                "logical expression"));
        }

        if (expression.Operator is LogicalOperator.And or LogicalOperator.Or && expression.Operands.Count == 0)
        {
            failures.Add(Failure(
                "empty-logical-operands",
                $"{expression.Operator} logical expressions require at least one operand.",
                $"{path}.Operands",
                "logical expression"));
        }

        for (var index = 0; index < expression.Operands.Count; index++)
            ValidateFilter(expression.Operands[index], $"{path}.Operands[{index}]", failures);
    }

    private void ValidateSorts(IReadOnlyList<SortExpression> sorts, List<QueryValidationFailure> failures)
    {
        for (var index = 0; index < sorts.Count; index++)
        {
            var sort = sorts[index];
            var path = $"Sorts[{index}]";

            if (string.IsNullOrWhiteSpace(sort.AspectKey))
            {
                if (!capabilities!.SupportsMetadataSorting)
                {
                    failures.Add(Failure(
                        "unsupported-metadata-sort",
                        $"Metadata sorting is not supported by {capabilities.ProviderName}.",
                        path,
                        "sort"));
                }

                if (!capabilities.SupportedMetadataFields.Contains(sort.Field))
                {
                    failures.Add(Failure(
                        "unsupported-metadata-sort-field",
                        $"Metadata sort field '{sort.Field}' is not supported by {capabilities.ProviderName}.",
                        $"{path}.Field",
                        "metadata field"));
                }
            }
            else if (!capabilities!.SupportsFacetSorting)
            {
                failures.Add(Failure(
                    "unsupported-facet-sort",
                    $"Facet sorting is not supported by {capabilities.ProviderName}.",
                    path,
                    "sort"));
            }
        }
    }

    private bool ValidateFilterType(QueryFilterType filterType, string path, List<QueryValidationFailure> failures)
    {
        if (capabilities!.SupportedFilterTypes.Contains(filterType))
            return true;

        failures.Add(Failure(
            "unsupported-filter-type",
            $"Filter type '{filterType}' is not supported by {capabilities.ProviderName}.",
            path,
            "predicate"));
        return false;
    }

    private void ValidateComparison(
        QueryFilterType filterType,
        ComparisonOperator comparisonOperator,
        string path,
        List<QueryValidationFailure> failures)
    {
        if (Enum.IsDefined(comparisonOperator)
            && capabilities!.SupportsComparison(filterType, comparisonOperator))
            return;

        failures.Add(Failure(
            "unsupported-comparison-operator",
            $"Comparison operator '{comparisonOperator}' is not supported for {filterType} filters by {capabilities!.ProviderName}.",
            path,
            "comparison operator"));
    }

    private void ValidateRangeValue(object value, string path, List<QueryValidationFailure> failures)
    {
        if (value is not RangeValue range)
        {
            failures.Add(Failure(
                "range-value-required",
                "Range predicates require a RangeValue.",
                path,
                "value shape"));
            return;
        }

        if (range.Min is null && range.Max is null)
        {
            failures.Add(Failure(
                "empty-range",
                "Range predicates require at least one bound.",
                path,
                "value shape"));
            return;
        }

        ValidateRangeBound(range.Min, $"{path}.Min", failures);
        ValidateRangeBound(range.Max, $"{path}.Max", failures);
    }

    private void ValidateRangeBound(object? value, string path, List<QueryValidationFailure> failures)
    {
        if (value is null)
            return;

        var shape = ResolveValueShape(value);
        if (shape is not null && capabilities!.FacetRangeSupport.Contains(shape.Value))
            return;

        failures.Add(Failure(
            "unsupported-range-value-shape",
            $"Range value shape '{shape?.ToString() ?? value.GetType().Name}' is not supported by {capabilities!.ProviderName}.",
            path,
            "value shape"));
    }

    private static QueryValueShape? ResolveValueShape(object value)
    {
        if (value is DateTime or DateTimeOffset or DateOnly)
            return QueryValueShape.DateTime;

        if (value is string stringValue)
        {
            if (decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                return QueryValueShape.Numeric;

            if (DateTime.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out _))
                return QueryValueShape.DateTime;

            return QueryValueShape.String;
        }

        if (value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
            return QueryValueShape.Numeric;

        return null;
    }

    private static QueryValidationFailure Failure(
        string code,
        string message,
        string? path = null,
        string? feature = null) =>
        new(code, message, path, feature);
}
