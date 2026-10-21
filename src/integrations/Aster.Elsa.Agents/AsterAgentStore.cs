using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Elsa.Agents.Persistence.Contracts;
using Elsa.Agents.Persistence.Entities;
using Elsa.Agents.Persistence.Filters;
using Microsoft.Extensions.Options;
using ISystemClock = Elsa.Common.ISystemClock;
using ITenantAccessor = Elsa.Common.Multitenancy.ITenantAccessor;

namespace Aster.Elsa.Agents;

/// <summary>
/// Elsa's <see cref="IAgentStore"/> on Aster. Each agent is an <c>ElsaAgent</c> resource whose <c>ResourceId</c> is the
/// Elsa agent ID:
/// <list type="bullet">
/// <item><b>Writes</b> save a new resource version (version 1 on add) and, with
/// <see cref="AsterAgentStoreOptions.AutoActivate"/>, activate it in <see cref="AsterAgentStoreOptions.Channel"/>.</item>
/// <item><b>Reads</b> see, for each agent, the version active in the channel (the highest when several are), and skip
/// agents that are archived or soft-deleted.</item>
/// <item><b>Deletes</b> soft-delete: the versions, activations and history stay, and Aster's restore service brings the
/// agent back.</item>
/// <item><b>Agent references</b> (<c>AgentConfig.Agents</c>) are agent names in Elsa and resource IDs in Aster: a name is
/// stored as the ID of the agent with that name, preferring the agent visible in the channel, and read back as that
/// agent's current name. A name that matches no agent is stored as written, which Aster's reference validation rejects
/// under its default strict mode.</item>
/// <item><b>Tenants:</b> every call runs in the Aster tenant mapped from Elsa's current tenant
/// (<see cref="AsterTenantMapping"/>).</item>
/// </list>
/// </summary>
internal sealed class AsterAgentStore(
    IResourceManager resources,
    IResourceQueryService queries,
    IResourceVersionReader versions,
    IResourceLifecycleMarkerService markers,
    ITypedAspectBinder binder,
    AgentDefinitionRegistrar registrar,
    IOptions<AsterAgentStoreOptions> options,
    ITenantAccessor? tenantAccessor = null,
    ISystemClock? clock = null) : IAgentStore
{
    private const int UpdateAttempts = 3;
    private const string AgentNameFacet = "Name";

    private string Channel => options.Value.Channel;

    /// <inheritdoc />
    public async Task AddAsync(AgentDefinition entity, CancellationToken cancellationToken = default) =>
        await SaveNewAsync(entity, activate: options.Value.AutoActivate, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateAsync(AgentDefinition entity, CancellationToken cancellationToken = default) =>
        await SaveAsync(entity, activate: options.Value.AutoActivate, cancellationToken);

    /// <inheritdoc />
    public async Task<AgentDefinition?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return (await FindAllAsync(new AgentDefinitionFilter { Id = id }, cancellationToken)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<AgentDefinition?> FindAsync(AgentDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return (await FindAllAsync(filter, cancellationToken)).FirstOrDefault();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default) =>
        await FindAllAsync(new AgentDefinitionFilter(), cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(AgentDefinition entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        await SoftDeleteAsync(entity.Id, CurrentTenant(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> DeleteManyAsync(AgentDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var tenant = CurrentTenant();
        var matches = await FindAllAsync(filter, cancellationToken);
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SoftDeleteAsync(match.Id, tenant, cancellationToken);
        }

        return matches.Count;
    }

    /// <summary>
    /// Saves version 1 of a new agent and, when <paramref name="activate"/> is set, activates it in the channel. An
    /// empty ID gets one from Aster, written back to <paramref name="entity"/>.
    /// </summary>
    /// <exception cref="DuplicateResourceIdException">An agent with the ID exists, including a deleted one.</exception>
    internal async Task<Resource> SaveNewAsync(AgentDefinition entity, bool activate, CancellationToken cancellationToken)
    {
        var tenant = PrepareWrite(entity);
        await registrar.EnsureRegisteredAsync(tenant, cancellationToken);
        var aspects = AgentAspects.ToAspects(entity, await ToAgentReferencesAsync(entity, tenant, cancellationToken));
        var created = await resources.CreateAsync(AsterAgentResource.DefinitionId, new CreateResourceRequest
        {
            TenantScope = tenant,
            ResourceId = string.IsNullOrEmpty(entity.Id) ? null : entity.Id,
            InitialAspects = aspects,
        }, cancellationToken);

        entity.Id = created.ResourceId;
        if (activate)
            await resources.ActivateAsync(created.ResourceId, created.Version, Channel, tenant, allowMultipleActive: false, cancellationToken);

        return created;
    }

    /// <summary>
    /// Saves a new version of an agent on top of its latest one (last writer wins, as in Elsa's stores), or version 1
    /// when the agent doesn't exist yet, and activates it when <paramref name="activate"/> is set.
    /// </summary>
    internal async Task<Resource> SaveAsync(AgentDefinition entity, bool activate, CancellationToken cancellationToken)
    {
        var tenant = PrepareWrite(entity);
        if (string.IsNullOrEmpty(entity.Id))
            return await SaveNewAsync(entity, activate, cancellationToken);

        await registrar.EnsureRegisteredAsync(tenant, cancellationToken);
        var aspects = AgentAspects.ToAspects(entity, await ToAgentReferencesAsync(entity, tenant, cancellationToken));
        for (var attempt = 1; ; attempt++)
        {
            var latest = await resources.GetLatestVersionAsync(entity.Id, tenant, cancellationToken);
            if (latest is null)
                return await SaveNewAsync(entity, activate, cancellationToken);

            try
            {
                var saved = await resources.UpdateAsync(entity.Id, new UpdateResourceRequest
                {
                    TenantScope = tenant,
                    BaseVersion = latest.Version,
                    AspectUpdates = aspects,
                }, cancellationToken);

                if (activate)
                    await resources.ActivateAsync(saved.ResourceId, saved.Version, Channel, tenant, allowMultipleActive: false, cancellationToken);

                return saved;
            }
            catch (ConcurrencyException) when (attempt < UpdateAttempts)
            {
                // Another writer saved a version in between; save on top of it, as Elsa's own stores would.
            }
        }
    }

    /// <summary>The Aster tenant mapped from Elsa's current tenant.</summary>
    internal TenantScope CurrentTenant() => AsterTenantMapping.FromTenantAccessor(tenantAccessor);

    /// <summary>
    /// The agents visible in the channel that match <paramref name="filter"/>, with Elsa's filter semantics (ordinal
    /// comparisons; an empty filter matches every agent), ordered by ID.
    /// </summary>
    internal async Task<IReadOnlyList<AgentDefinition>> FindAllAsync(AgentDefinitionFilter filter, CancellationToken cancellationToken)
    {
        var tenant = CurrentTenant();

        // Providers compare text case-insensitively, so the query narrows and Matches decides, ordinally. NotId stays out
        // of the query: a case-insensitive "not equal" would drop agents whose IDs differ only in case.
        var predicates = new List<FilterExpression>();
        if (!string.IsNullOrWhiteSpace(filter.Id))
            predicates.Add(new MetadataFilter("ResourceId", filter.Id, ComparisonOperator.Equals));
        if (filter.Ids is { Count: > 0 } ids)
            predicates.Add(new MetadataFilter("ResourceId", ids.ToArray(), ComparisonOperator.In));
        if (!string.IsNullOrWhiteSpace(filter.Name))
            predicates.Add(new FacetValueFilter(AsterAgentResource.AgentAspectKey, AgentNameFacet, filter.Name, ComparisonOperator.Equals));

        var visible = (await QueryVisibleAsync(tenant, predicates, cancellationToken))
            .Select(resource => AgentAspects.Decode(resource, binder))
            .Where(agent => Matches(filter, agent))
            .ToList();

        return await ToEntitiesAsync(visible, tenant, cancellationToken);
    }

    /// <summary>
    /// The version of each agent visible in the channel: active there, not archived or soft-deleted, and the highest
    /// when several versions are active (an A/B test). Ordered by resource ID.
    /// </summary>
    private async Task<List<Resource>> QueryVisibleAsync(TenantScope tenant, IReadOnlyList<FilterExpression> predicates, CancellationToken cancellationToken)
    {
        var rows = await queries.QueryAsync(new ResourceQuery
        {
            TenantScope = tenant,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = Channel,
            DefinitionId = AsterAgentResource.DefinitionId,
            LifecycleState = ResourceLifecycleMarkerState.None,
            Filter = predicates.Count switch
            {
                0 => null,
                1 => predicates[0],
                _ => new LogicalExpression(LogicalOperator.And, predicates),
            },
        }, cancellationToken);

        return rows
            .GroupBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .Select(static versions => versions.MaxBy(static resource => resource.Version)!)
            .OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal)
            .ToList();
    }

    private static bool Matches(AgentDefinitionFilter filter, StoredAgent agent)
    {
        var id = agent.Resource.ResourceId;
        if (!string.IsNullOrWhiteSpace(filter.Id) && !string.Equals(id, filter.Id, StringComparison.Ordinal))
            return false;
        if (filter.Ids is { Count: > 0 } ids && !ids.Contains(id))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.NotId) && string.Equals(id, filter.NotId, StringComparison.Ordinal))
            return false;
        if (!string.IsNullOrWhiteSpace(filter.Name) && !string.Equals(agent.Agent.Name, filter.Name, StringComparison.Ordinal))
            return false;
        return true;
    }

    /// <summary>
    /// Elsa entities for stored versions, with agent references translated from resource IDs back to agent names: the
    /// name in the channel when the referenced agent is active there, otherwise its latest name.
    /// </summary>
    private async Task<IReadOnlyList<AgentDefinition>> ToEntitiesAsync(IReadOnlyList<StoredAgent> visible, TenantScope tenant, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var agent in visible)
            names[agent.Resource.ResourceId] = agent.Agent.Name;

        var unknown = visible
            .SelectMany(static agent => agent.Config.Agents ?? [])
            .Where(id => !string.IsNullOrEmpty(id) && !names.ContainsKey(id))
            .ToHashSet(StringComparer.Ordinal);

        if (unknown.Count > 0)
        {
            foreach (var scope in new[] { ResourceVersionScope.Active, ResourceVersionScope.Latest })
            {
                var found = await versions.ReadVersionsAsync(new ResourceVersionReadRequest
                {
                    TenantScope = tenant,
                    Scope = scope,
                    ActivationChannel = scope == ResourceVersionScope.Active ? Channel : null,
                    ResourceIds = unknown,
                }, cancellationToken);

                foreach (var resource in found
                             .Where(static resource => resource.DefinitionId == AsterAgentResource.DefinitionId)
                             .OrderByDescending(static resource => resource.Version))
                {
                    if (names.TryAdd(resource.ResourceId, AgentAspects.ReadName(resource, binder)))
                        unknown.Remove(resource.ResourceId);
                }

                if (unknown.Count == 0)
                    break;
            }
        }

        var elsaTenantId = tenantAccessor?.TenantId;
        return visible
            .Select(agent => AgentAspects.ToEntity(agent, elsaTenantId,
                reference => names.TryGetValue(reference, out var name) && name is not null ? name : reference))
            .ToList();
    }

    /// <summary>
    /// The stored form of the entity's agent references: each name becomes the resource ID of the agent with that name
    /// (the one visible in the channel first, then any agent that is not deleted, lowest ID first); a name that matches
    /// no agent stays as written.
    /// </summary>
    private async Task<string[]?> ToAgentReferencesAsync(AgentDefinition entity, TenantScope tenant, CancellationToken cancellationToken)
    {
        if (entity.AgentConfig.Agents is not { } references)
            return null;

        var pending = references.Where(static name => !string.IsNullOrEmpty(name)).ToHashSet(StringComparer.Ordinal);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        if (pending.Count > 0)
        {
            var nameIn = new FacetValueFilter(AsterAgentResource.AgentAspectKey, AgentNameFacet, pending.ToArray(), ComparisonOperator.In);
            foreach (var resource in await QueryVisibleAsync(tenant, [nameIn], cancellationToken))
                if (AgentAspects.ReadName(resource, binder) is { } name && pending.Contains(name))
                    ids.TryAdd(name, resource.ResourceId);

            pending.ExceptWith(ids.Keys);
        }

        if (pending.Count > 0)
        {
            var latest = await queries.QueryAsync(new ResourceQuery
            {
                TenantScope = tenant,
                Scope = ResourceVersionScope.Latest,
                DefinitionId = AsterAgentResource.DefinitionId,
                LifecycleState = ResourceLifecycleMarkerState.None,
                Filter = new FacetValueFilter(AsterAgentResource.AgentAspectKey, AgentNameFacet, pending.ToArray(), ComparisonOperator.In),
            }, cancellationToken);

            foreach (var resource in latest.OrderBy(static resource => resource.ResourceId, StringComparer.Ordinal))
                if (AgentAspects.ReadName(resource, binder) is { } name && pending.Contains(name))
                    ids.TryAdd(name, resource.ResourceId);
        }

        return references.Select(name => ids.TryGetValue(name ?? "", out var id) ? id : name!).ToArray();
    }

    private TenantScope PrepareWrite(AgentDefinition entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.AgentConfig is null)
            throw new ArgumentException($"Agent '{entity.Id}' has no AgentConfig. Elsa builds every agent from its AgentConfig, so the Aster agent store requires one.", nameof(entity));

        var tenant = CurrentTenant();
        entity.TenantId ??= tenantAccessor?.TenantId;
        return tenant;
    }

    private async Task SoftDeleteAsync(string? id, TenantScope tenant, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(id))
            return;

        // As in Elsa's stores, deleting an agent that doesn't exist, or is already gone, does nothing.
        if (await resources.GetLatestVersionAsync(id, tenant, cancellationToken) is null)
            return;
        if (await markers.GetAsync(id, tenant, cancellationToken) is { State: not ResourceLifecycleMarkerState.None })
            return;

        var result = await markers.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = tenant,
            ResourceId = id,
            State = ResourceLifecycleMarkerState.SoftDeleted,
            MarkedAt = clock?.UtcNow ?? DateTimeOffset.UtcNow,
            Reason = "Deleted through Elsa's agent store.",
        }, cancellationToken);

        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Agent '{id}' could not be deleted: {string.Join("; ", result.Diagnostics.Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"))}");
    }
}
