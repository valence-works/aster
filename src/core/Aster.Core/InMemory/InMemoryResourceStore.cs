using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory store for <see cref="Resource"/> versions and activation state.
/// Intended for use by <see cref="InMemoryResourceManager"/> only.
/// </summary>
public sealed class InMemoryResourceStore : IResourceVersionReader, IResourceVersionWriter
{
    /// <summary>
    /// Resource version history keyed by tenant ID and <c>ResourceId</c>.
    /// Each list is ordered from V1 to the latest; access must be synchronised via <c>lock</c>.
    /// </summary>
    internal readonly ConcurrentDictionary<(string TenantId, string ResourceId), List<Resource>> Versions = [];

    /// <summary>
    /// Activation state keyed by tenant ID and <c>ResourceId</c> → channel name → set of active version numbers.
    /// The inner <see cref="ConcurrentDictionary{TKey,TValue}"/> is used as a lock target for
    /// atomic read-modify-write on the contained <see cref="HashSet{T}"/>.
    /// </summary>
    internal readonly ConcurrentDictionary<(string TenantId, string ResourceId), ConcurrentDictionary<string, HashSet<int>>> Activations = [];

    /// <summary>
    /// Last persisted activation state keyed by tenant ID and <c>ResourceId</c> → channel name.
    /// </summary>
    internal readonly ConcurrentDictionary<(string TenantId, string ResourceId), ConcurrentDictionary<string, ActivationState>> ActivationStates = [];

    /// <summary>
    /// Returns the ordered version list for a resource, or <see langword="null"/> if it does not exist.
    /// The caller must <c>lock</c> the returned list when reading or mutating.
    /// </summary>
    internal List<Resource>? TryGetVersions(string resourceId) =>
        TryGetVersions(resourceId, TenantScope.Default);

    /// <summary>
    /// Returns the ordered version list for a tenant-scoped resource, or <see langword="null"/> if it does not exist.
    /// </summary>
    internal List<Resource>? TryGetVersions(string resourceId, TenantScope tenantScope)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        return Versions.TryGetValue((tenant.TenantId, resourceId), out var list) ? list : null;
    }

    /// <summary>
    /// Returns the activation channel map for a resource, creating it if absent.
    /// </summary>
    internal ConcurrentDictionary<string, HashSet<int>> GetOrAddActivations(string resourceId) =>
        GetOrAddActivations(resourceId, TenantScope.Default);

    /// <summary>
    /// Returns the activation channel map for a tenant-scoped resource, creating it if absent.
    /// </summary>
    internal ConcurrentDictionary<string, HashSet<int>> GetOrAddActivations(string resourceId, TenantScope tenantScope)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        return Activations.GetOrAdd((tenant.TenantId, resourceId), _ => new ConcurrentDictionary<string, HashSet<int>>(StringComparer.Ordinal));
    }

    /// <summary>
    /// Imports an exact resource version while preserving version ordering.
    /// </summary>
    internal void ImportVersion(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var tenant = TenantScopeResolver.Resolve(resource.TenantScope);

        var versions = Versions.GetOrAdd((tenant.TenantId, resource.ResourceId), _ => []);
        lock (versions)
        {
            var insertIndex = versions.FindIndex(existing => existing.Version >= resource.Version);
            if (insertIndex >= 0 && versions[insertIndex].Version == resource.Version)
                throw new InvalidOperationException($"Resource '{resource.ResourceId}' version {resource.Version} already exists.");

            if (insertIndex < 0)
                versions.Add(resource);
            else
                versions.Insert(insertIndex, resource);
        }
    }

    /// <summary>
    /// Removes an imported resource version during rollback.
    /// </summary>
    internal void RemoveImportedVersion(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var tenant = TenantScopeResolver.Resolve(resource.TenantScope);

        if (!Versions.TryGetValue((tenant.TenantId, resource.ResourceId), out var versions))
            return;

        lock (versions)
            versions.RemoveAll(existing =>
                existing.Version == resource.Version
                && string.Equals(existing.Id, resource.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns the persisted activation state for a resource/channel pair.
    /// </summary>
    internal ActivationState? GetActivationState(string resourceId, string channel) =>
        GetActivationState(resourceId, channel, TenantScope.Default);

    /// <summary>
    /// Returns the persisted activation state for a tenant-scoped resource/channel pair.
    /// </summary>
    internal ActivationState? GetActivationState(string resourceId, string channel, TenantScope tenantScope)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        return ActivationStates.TryGetValue((tenant.TenantId, resourceId), out var states)
        && states.TryGetValue(channel, out var state)
            ? state
            : null;
    }

    /// <summary>
    /// Restores a persisted activation state, or removes it when <paramref name="state"/> is <see langword="null"/>.
    /// </summary>
    internal void RestoreActivationState(string resourceId, string channel, ActivationState? state)
    {
        var tenantScope = state?.TenantScope ?? TenantScope.Default;
        RestoreActivationState(resourceId, channel, tenantScope, state);
    }

    /// <summary>
    /// Restores a tenant-scoped persisted activation state, or removes it when <paramref name="state"/> is <see langword="null"/>.
    /// </summary>
    internal void RestoreActivationState(string resourceId, string channel, TenantScope tenantScope, ActivationState? state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var channelActivations = GetOrAddActivations(resourceId, tenant);
        lock (channelActivations)
        {
            if (state is null)
                channelActivations.TryRemove(channel, out _);
            else
                channelActivations[channel] = state.ActiveVersions.ToHashSet();
        }

        var states = ActivationStates.GetOrAdd(
            (tenant.TenantId, resourceId),
            _ => new ConcurrentDictionary<string, ActivationState>(StringComparer.Ordinal));

        if (state is null)
            states.TryRemove(channel, out _);
        else
            states[channel] = state;
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
        ResourceVersionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = TenantScopeResolver.Resolve(request.TenantScope);

        var resources = request.Scope switch
        {
            ResourceVersionScope.Latest => ReadLatestVersions(tenant, cancellationToken),
            ResourceVersionScope.AllVersions => ReadAllVersions(tenant, cancellationToken),
            ResourceVersionScope.Active => ReadActiveVersions(tenant, request.ActivationChannel, cancellationToken),
            ResourceVersionScope.Draft => ReadDraftVersions(tenant, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Scope, "Unknown resource version scope.")
        };

        return ValueTask.FromResult<IEnumerable<Resource>>(resources.ToList());
    }

    /// <inheritdoc />
    public ValueTask<Resource> SaveVersionAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        var tenant = TenantScopeResolver.Resolve(resource.TenantScope);
        var scopedResource = resource with { TenantScope = tenant };

        var versions = Versions.GetOrAdd((tenant.TenantId, scopedResource.ResourceId), _ => []);
        lock (versions)
        {
            versions.Add(scopedResource);
        }

        return ValueTask.FromResult(scopedResource);
    }

    /// <inheritdoc />
    public ValueTask<ActivationState> UpdateActivationAsync(
        string resourceId,
        string channel,
        ActivationState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        var tenant = TenantScopeResolver.Resolve(state.TenantScope);
        var scopedState = state with { TenantScope = tenant };

        var channelActivations = GetOrAddActivations(resourceId, tenant);
        lock (channelActivations)
            channelActivations[channel] = scopedState.ActiveVersions.ToHashSet();

        var states = ActivationStates.GetOrAdd(
            (tenant.TenantId, resourceId),
            _ => new ConcurrentDictionary<string, ActivationState>(StringComparer.Ordinal));
        states[channel] = scopedState;

        return ValueTask.FromResult(scopedState);
    }

    /// <summary>
    /// Returns all resource IDs that belong to the specified definition.
    /// </summary>
    internal IEnumerable<string> GetResourceIdsForDefinition(string definitionId) =>
        GetResourceIdsForDefinition(definitionId, TenantScope.Default);

    /// <summary>
    /// Returns all resource IDs that belong to the specified definition in a tenant.
    /// </summary>
    internal IEnumerable<string> GetResourceIdsForDefinition(string definitionId, TenantScope tenantScope)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        foreach (var ((tenantId, resourceId), list) in Versions)
        {
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            lock (list)
            {
                if (list.Count > 0 && string.Equals(list[0].DefinitionId, definitionId, StringComparison.Ordinal))
                    yield return resourceId;
            }
        }
    }

    private IEnumerable<Resource> ReadLatestVersions(TenantScope tenant, CancellationToken cancellationToken)
    {
        foreach (var ((tenantId, _), versionList) in Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            lock (versionList)
            {
                if (versionList.Count > 0)
                    yield return versionList[^1];
            }
        }
    }

    private IEnumerable<Resource> ReadAllVersions(TenantScope tenant, CancellationToken cancellationToken)
    {
        foreach (var ((tenantId, _), versionList) in Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            List<Resource> snapshot;
            lock (versionList)
                snapshot = [.. versionList];

            foreach (var resource in snapshot)
                yield return resource;
        }
    }

    private IEnumerable<Resource> ReadActiveVersions(TenantScope tenant, string? channel, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        foreach (var ((tenantId, resourceId), versionList) in Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            if (!Activations.TryGetValue((tenant.TenantId, resourceId), out var channelActivations))
                continue;

            HashSet<int> activeVersionNumbers;
            lock (channelActivations)
            {
                activeVersionNumbers = channelActivations.TryGetValue(channel, out var active)
                    ? new HashSet<int>(active)
                    : [];
            }

            if (activeVersionNumbers.Count == 0)
                continue;

            List<Resource> snapshot;
            lock (versionList)
                snapshot = versionList.Where(r => activeVersionNumbers.Contains(r.Version)).ToList();

            foreach (var resource in snapshot)
                yield return resource;
        }
    }

    private IEnumerable<Resource> ReadDraftVersions(TenantScope tenant, CancellationToken cancellationToken)
    {
        var activeVersionsByResource = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (var ((tenantId, resourceId), channelActivations) in Activations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            lock (channelActivations)
            {
                activeVersionsByResource[resourceId] = channelActivations.Values
                    .SelectMany(static versions => versions)
                    .ToHashSet();
            }
        }

        foreach (var ((tenantId, resourceId), versionList) in Versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            activeVersionsByResource.TryGetValue(resourceId, out var activeVersionNumbers);

            List<Resource> snapshot;
            lock (versionList)
            {
                snapshot = versionList
                    .Where(r => activeVersionNumbers?.Contains(r.Version) != true)
                    .ToList();
            }

            foreach (var resource in snapshot)
                yield return resource;
        }
    }
}
