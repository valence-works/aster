using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;

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
    IResourceQueryCapabilitiesProvider capabilitiesProvider,
    IResourceQueryValidator queryValidator) : IResourceReferenceService
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

        var tenant = TenantScope.Resolve(query.TenantScope);
        var scope = new ResourceQuery
        {
            TenantScope = tenant,
            Scope = query.Scope,
            ActivationChannel = query.ActivationChannel,
            LifecycleState = query.LifecycleState,
        };

        // Validate the scope before the empty-result shortcuts, so Take = 0 or a tenant without reference
        // facets rejects the same requests as a lookup that queries.
        var validation = queryValidator.Validate(scope);
        if (!validation.IsValid)
            throw UnsupportedQueryFeatureException.FromValidationFailure(validation.Failures[0]);

        if (query.Take == 0)
            return new ResourceReferrerPage([], false);

        var facetsByDefinition = new Dictionary<string, IReadOnlyList<ReferenceFacetValues.ReferenceFacet>>(StringComparer.Ordinal);
        var branches = new List<FilterExpression>();
        foreach (var definition in (await definitionStore.ListDefinitionsAsync(tenant, cancellationToken))
                     .OrderBy(static definition => definition.DefinitionId, StringComparer.Ordinal))
        {
            if (query.DefinitionId is { } only && !string.Equals(definition.DefinitionId, only, StringComparison.Ordinal))
                continue;

            // Every reference facet, whatever its TargetDefinitionIds: a lenient save can store a target of
            // another definition, and a reverse lookup must still find it.
            var facets = ReferenceFacetValues.Facets(definition);
            if (facets.Count == 0)
                continue;

            facetsByDefinition[definition.DefinitionId] = facets;
            branches.Add(new LogicalExpression(LogicalOperator.And,
            [
                new MetadataFilter("DefinitionId", definition.DefinitionId, ComparisonOperator.Equals),
                AnyOf(facets.Select(facet => Filter(facet, targetResourceId)).ToList()),
            ]));
        }

        if (branches.Count == 0)
            return new ResourceReferrerPage([], false);

        var candidates = scope with
        {
            Filter = AnyOf(branches),
            Sorts = [new SortExpression("ResourceId"), new SortExpression("Version")],
        };

        // Providers compare text case-insensitively, but ResourceIds are ordinal, so each candidate is confirmed
        // here and paging applies to confirmed referrers. Candidates are read in batches until the page plus one
        // lookahead referrer is confirmed; a further batch is needed only when another ResourceId differs from
        // the target by letter case alone.
        var skip = query.Skip ?? 0;
        var wanted = query.Take is { } take ? (long)skip + take + 1 : long.MaxValue;
        int? batchSize = query.Take is null ? null : (int)Math.Min(wanted, int.MaxValue);
        var referrers = new List<ResourceReferrer>();
        var offset = 0L;
        while (offset <= int.MaxValue)
        {
            var batch = (await queryService.QueryAsync(candidates with { Skip = (int)offset, Take = batchSize }, cancellationToken)).ToList();
            foreach (var version in batch)
            {
                if (!facetsByDefinition.TryGetValue(version.DefinitionId, out var facets))
                    continue;

                var locations = Locate(version, facets, targetResourceId);
                if (locations.Count > 0)
                    referrers.Add(new ResourceReferrer(version, locations));
            }

            if (batchSize is not { } size || batch.Count < size || referrers.Count >= wanted)
                break;

            offset += batch.Count;
        }

        var start = Math.Min(skip, referrers.Count);
        var available = referrers.Count - start;
        var count = query.Take is { } pageSize ? Math.Min(pageSize, available) : available;
        return new ResourceReferrerPage(referrers.GetRange(start, count), available > count);
    }

    private static List<ResourceReferenceLocation> Locate(
        Resource version,
        IReadOnlyList<ReferenceFacetValues.ReferenceFacet> facets,
        string targetResourceId) =>
        ReferenceFacetValues.Read(facets, version.Aspects)
            .Where(value => string.Equals(value.TargetResourceId, targetResourceId, StringComparison.Ordinal))
            .Select(static value => new ResourceReferenceLocation(value.Facet.AspectKey, value.Facet.FacetDefinitionId, value.ElementIndex))
            .ToList();

    private static FilterExpression Filter(ReferenceFacetValues.ReferenceFacet facet, string targetResourceId) =>
        new FacetValueFilter(
            facet.AspectKey,
            facet.FacetDefinitionId,
            targetResourceId,
            facet.IsArray ? ComparisonOperator.ArrayContains : ComparisonOperator.Equals);

    private static FilterExpression AnyOf(IReadOnlyList<FilterExpression> operands) =>
        operands.Count == 1 ? operands[0] : new LogicalExpression(LogicalOperator.Or, operands);
}
