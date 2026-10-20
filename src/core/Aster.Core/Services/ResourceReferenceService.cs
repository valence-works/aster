using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IResourceReferenceService"/>. It turns the reference facets declared by the
/// tenant's definitions into one portable <see cref="ResourceQuery"/>
/// (<c>DefinitionId</c> plus <see cref="ComparisonOperator.Equals"/> or
/// <see cref="ComparisonOperator.ArrayContains"/> per facet), so every provider answers it with its own
/// query engine: an in-memory scan, or JSON path predicates in SQLite.
/// </summary>
/// <remarks>
/// Cost: SQLite evaluates the predicates per row, so a lookup is a scan of the searched scope
/// (latest or active versions in the tenant). Providers with index projections (#100, #65) can
/// serve the same <see cref="FacetValueFilter"/> predicates from an index without changing this service.
/// </remarks>
public sealed class ResourceReferenceService(
    IResourceDefinitionStore definitionStore,
    IResourceQueryService queryService,
    IResourceQueryCapabilitiesProvider capabilitiesProvider) : IResourceReferenceService
{
    /// <inheritdoc />
    public async ValueTask<ResourceReferrerPage> GetReferrersAsync(
        string targetResourceId,
        ResourceReferrerQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);
        query ??= new ResourceReferrerQuery();
        if (query.Skip is < 0)
            throw new ArgumentOutOfRangeException(nameof(query), query.Skip, "Skip must not be negative.");
        if (query.Take is < 0)
            throw new ArgumentOutOfRangeException(nameof(query), query.Take, "Take must not be negative.");

        if (!capabilitiesProvider.Capabilities.SupportsReverseReferenceLookup)
        {
            throw new NotSupportedException(
                $"The query provider '{capabilitiesProvider.Capabilities.ProviderKey}' does not support reverse reference lookup.");
        }

        if (query.Take == 0)
            return new ResourceReferrerPage([], false);

        var tenant = TenantScope.Resolve(query.TenantScope);

        var facetsByDefinition = new Dictionary<string, IReadOnlyList<ReferenceFacet>>(StringComparer.Ordinal);
        var branches = new List<FilterExpression>();
        foreach (var definition in (await definitionStore.ListDefinitionsAsync(tenant, cancellationToken))
                     .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal))
        {
            if (query.DefinitionId is { } only && !string.Equals(definition.DefinitionId, only, StringComparison.Ordinal))
                continue;

            var facets = ReferenceFacets(definition);
            if (facets.Count == 0)
                continue;

            facetsByDefinition[definition.DefinitionId] = facets;
            branches.Add(new LogicalExpression(LogicalOperator.And,
            [
                new MetadataFilter("DefinitionId", definition.DefinitionId, ComparisonOperator.Equals),
                AnyOf(facets.Select(facet => facet.Filter(targetResourceId)).ToList()),
            ]));
        }

        if (branches.Count == 0)
            return new ResourceReferrerPage([], false);

        var versions = (await queryService.QueryAsync(new ResourceQuery
        {
            TenantScope = tenant,
            Scope = query.Scope,
            ActivationChannel = query.ActivationChannel,
            LifecycleState = query.LifecycleState,
            Filter = AnyOf(branches),
            Sorts = [new SortExpression("ResourceId"), new SortExpression("Version")],
            Skip = query.Skip,
            Take = query.Take + 1,
        }, cancellationToken)).ToList();

        var hasMore = query.Take is { } take && versions.Count > take;
        if (hasMore)
            versions.RemoveAt(versions.Count - 1);

        // Providers compare text case-insensitively; ResourceIds are ordinal, so confirm each match here.
        var referrers = new List<ResourceReferrer>(versions.Count);
        foreach (var version in versions)
        {
            if (!facetsByDefinition.TryGetValue(version.DefinitionId, out var facets))
                continue;

            var locations = Locate(version, facets, targetResourceId);
            if (locations.Count > 0)
                referrers.Add(new ResourceReferrer(version, locations));
        }

        return new ResourceReferrerPage(referrers, hasMore);
    }

    /// <summary>
    /// Every reference facet, whatever its <c>TargetDefinitionIds</c>: a lenient save can store a target
    /// of another definition, and a reverse lookup must still find it.
    /// </summary>
    private static IReadOnlyList<ReferenceFacet> ReferenceFacets(ResourceDefinition definition)
    {
        var facets = new List<ReferenceFacet>();
        foreach (var (aspectKey, aspect) in definition.AspectDefinitions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            foreach (var facet in aspect.FacetDefinitions.OrderBy(static facet => facet.FacetDefinitionId, StringComparer.Ordinal))
            {
                var descriptor = ParseFacetType(facet.Type);
                if (descriptor.Element == FacetValueType.Reference)
                    facets.Add(new ReferenceFacet(aspectKey, facet.FacetDefinitionId, descriptor.IsArray));
            }
        }

        return facets;
    }

    private static List<ResourceReferenceLocation> Locate(Resource version, IReadOnlyList<ReferenceFacet> facets, string targetResourceId)
    {
        var locations = new List<ResourceReferenceLocation>();
        foreach (var facet in facets)
        {
            if (!version.Aspects.TryGetValue(facet.AspectKey, out var payload)
                || payload is null
                || !AspectFacetValueResolver.TryResolveValue(payload, facet.FacetDefinitionId, out var rawValue))
            {
                continue;
            }

            if (!facet.IsArray)
            {
                if (Names(rawValue, targetResourceId))
                    locations.Add(new ResourceReferenceLocation(facet.AspectKey, facet.FacetDefinitionId, null));
            }
            else if (TryGetArrayElements(rawValue, out var elements))
            {
                for (var index = 0; index < elements.Count; index++)
                {
                    if (Names(elements[index], targetResourceId))
                        locations.Add(new ResourceReferenceLocation(facet.AspectKey, facet.FacetDefinitionId, index));
                }
            }
        }

        return locations;
    }

    private static bool Names(object? value, string targetResourceId) =>
        TryNormalizeValue(value, FacetValueType.Reference, out var normalized)
        && string.Equals((string)normalized, targetResourceId, StringComparison.Ordinal);

    private static FilterExpression AnyOf(IReadOnlyList<FilterExpression> operands) =>
        operands.Count == 1 ? operands[0] : new LogicalExpression(LogicalOperator.Or, operands);

    private sealed record ReferenceFacet(string AspectKey, string FacetDefinitionId, bool IsArray)
    {
        public FilterExpression Filter(string targetResourceId) =>
            new FacetValueFilter(
                AspectKey,
                FacetDefinitionId,
                targetResourceId,
                IsArray ? ComparisonOperator.ArrayContains : ComparisonOperator.Equals);
    }
}
