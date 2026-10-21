using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IAspectDefinitionStore"/>.
/// Stores ordered version snapshots per tenant and <c>AspectDefinitionId</c>.
/// </summary>
public sealed class InMemoryAspectDefinitionStore : IAspectDefinitionStore
{
    private readonly ConcurrentDictionary<(string TenantId, string AspectDefinitionId), List<AspectDefinition>> aspects = [];

    /// <inheritdoc />
    public ValueTask<AspectDefinition> RegisterAsync(
        AspectDefinition aspect,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        ArgumentException.ThrowIfNullOrWhiteSpace(aspect.AspectDefinitionId);
        FacetMetadataValidator.Validate(aspect);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = aspects.GetOrAdd((tenant.TenantId, aspect.AspectDefinitionId), static _ => []);
        lock (versions)
        {
            // Snapshot the facet list so a caller mutating the collection it registered can't change a stored
            // version (and every definition pinned to it), matching the detached copies SQLite returns.
            var registered = aspect with
            {
                Version = versions.Count > 0 ? versions[^1].Version + 1 : 1,
                IsShared = false,
                FacetDefinitions = Array.AsReadOnly(aspect.FacetDefinitions.ToArray()),
            };
            versions.Add(registered);
            return ValueTask.FromResult(registered);
        }
    }

    /// <inheritdoc />
    public ValueTask<AspectDefinition?> GetLatestAsync(
        string aspectDefinitionId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);

        return ValueTask.FromResult(Versions(aspectDefinitionId, tenantScope).LastOrDefault());
    }

    /// <inheritdoc />
    public ValueTask<AspectDefinition?> GetVersionAsync(
        string aspectDefinitionId,
        int version,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);

        return ValueTask.FromResult(Versions(aspectDefinitionId, tenantScope).FirstOrDefault(candidate => candidate.Version == version));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AspectDefinition>> ListAsync(
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        IReadOnlyList<AspectDefinition> latest = aspects
            .Where(pair => string.Equals(pair.Key.TenantId, tenant.TenantId, StringComparison.Ordinal))
            .OrderBy(static pair => pair.Key.AspectDefinitionId, StringComparer.Ordinal)
            .Select(static pair =>
            {
                lock (pair.Value)
                    return pair.Value.Count > 0 ? pair.Value[^1] : null;
            })
            .OfType<AspectDefinition>()
            .ToList();

        return ValueTask.FromResult(latest);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AspectDefinition>> ListVersionsAsync(
        string aspectDefinitionId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectDefinitionId);

        return ValueTask.FromResult<IReadOnlyList<AspectDefinition>>(Versions(aspectDefinitionId, tenantScope));
    }

    /// <summary>
    /// Inserts an imported version with its version number preserved, keeping versions ordered. Later
    /// registrations continue from the highest version.
    /// </summary>
    internal void ImportVersion(AspectDefinition aspect, TenantScope tenantScope)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = aspects.GetOrAdd((tenant.TenantId, aspect.AspectDefinitionId), static _ => []);
        lock (versions)
        {
            var insertIndex = versions.FindIndex(existing => existing.Version >= aspect.Version);
            if (insertIndex >= 0 && versions[insertIndex].Version == aspect.Version)
                throw new InvalidOperationException($"Aspect definition '{aspect.AspectDefinitionId}' version {aspect.Version} already exists.");

            var imported = aspect with
            {
                IsShared = false,
                FacetDefinitions = Array.AsReadOnly(aspect.FacetDefinitions.ToArray()),
            };

            if (insertIndex < 0)
                versions.Add(imported);
            else
                versions.Insert(insertIndex, imported);
        }
    }

    /// <summary>
    /// Removes a version written by <see cref="ImportVersion"/>, to roll back a failed import.
    /// </summary>
    internal void RemoveImportedVersion(AspectDefinition aspect, TenantScope tenantScope)
    {
        ArgumentNullException.ThrowIfNull(aspect);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        if (!aspects.TryGetValue((tenant.TenantId, aspect.AspectDefinitionId), out var versions))
            return;

        lock (versions)
            versions.RemoveAll(existing =>
                existing.Version == aspect.Version
                && string.Equals(existing.Id, aspect.Id, StringComparison.Ordinal));
    }

    /// <summary>
    /// Returns one version synchronously, or <see langword="null"/> when it does not exist.
    /// </summary>
    internal AspectDefinition? GetVersion(string aspectDefinitionId, int version, TenantScope tenantScope) =>
        Versions(aspectDefinitionId, tenantScope).FirstOrDefault(candidate => candidate.Version == version);

    private List<AspectDefinition> Versions(string aspectDefinitionId, TenantScope? tenantScope)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        if (!aspects.TryGetValue((tenant.TenantId, aspectDefinitionId), out var versions))
            return [];

        lock (versions)
            return [.. versions];
    }
}
