using System.Collections.Concurrent;
using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Microsoft.Extensions.Logging;

namespace Aster.Core.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IResourceDefinitionStore"/>.
/// Stores ordered version snapshots per <c>DefinitionId</c>.
/// </summary>
public sealed partial class InMemoryResourceDefinitionStore : IResourceDefinitionStore
{
    private readonly ConcurrentDictionary<(string TenantId, string DefinitionId), List<ResourceDefinition>> definitions = [];
    private readonly ILogger<InMemoryResourceDefinitionStore> logger;

    /// <summary>
    /// Initializes a new instance of <see cref="InMemoryResourceDefinitionStore"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public InMemoryResourceDefinitionStore(ILogger<InMemoryResourceDefinitionStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
        GetDefinitionAsync(definitionId, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        if (definitions.TryGetValue((tenant.TenantId, definitionId), out var versions))
        {
            lock (versions)
            {
                return ValueTask.FromResult<ResourceDefinition?>(versions.Count > 0 ? versions[^1] : null);
            }
        }

        return ValueTask.FromResult<ResourceDefinition?>(null);
    }

    /// <inheritdoc />
    public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default) =>
        GetDefinitionVersionAsync(definitionId, version, TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(
        string definitionId,
        int version,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        if (definitions.TryGetValue((tenant.TenantId, definitionId), out var versions))
        {
            lock (versions)
            {
                var match = versions.FirstOrDefault(d => d.Version == version);
                return ValueTask.FromResult<ResourceDefinition?>(match);
            }
        }

        return ValueTask.FromResult<ResourceDefinition?>(null);
    }

    /// <inheritdoc />
    public ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default) =>
        RegisterDefinitionAsync(definition, definition.TenantScope, cancellationToken);

    /// <inheritdoc />
    public ValueTask RegisterDefinitionAsync(
        ResourceDefinition definition,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        var versions = definitions.GetOrAdd((tenant.TenantId, definition.DefinitionId), _ => []);

        lock (versions)
        {
            int nextVersion = versions.Count > 0 ? versions[^1].Version + 1 : 1;
            var versionedDefinition = definition with { TenantScope = tenant, Version = nextVersion };
            versions.Add(versionedDefinition);
            LogDefinitionRegistered(definition.DefinitionId, nextVersion);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default) =>
        ListDefinitionsAsync(TenantScope.Default, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(TenantScope tenantScope, CancellationToken cancellationToken = default)
    {
        var tenant = TenantScopeResolver.Resolve(tenantScope);
        var latest = new List<ResourceDefinition>();

        foreach (var ((tenantId, _), versions) in definitions)
        {
            if (!string.Equals(tenantId, tenant.TenantId, StringComparison.Ordinal))
                continue;

            lock (versions)
            {
                if (versions.Count > 0)
                    latest.Add(versions[^1]);
            }
        }

        return ValueTask.FromResult<IEnumerable<ResourceDefinition>>(latest);
    }

    /// <summary>
    /// Returns all versions for a definition.
    /// </summary>
    internal IReadOnlyList<ResourceDefinition> GetDefinitionVersions(string definitionId) =>
        GetDefinitionVersions(definitionId, TenantScope.Default);

    /// <summary>
    /// Returns all versions for a definition in a tenant.
    /// </summary>
    internal IReadOnlyList<ResourceDefinition> GetDefinitionVersions(string definitionId, TenantScope tenantScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        if (!definitions.TryGetValue((tenant.TenantId, definitionId), out var versions))
            return [];

        lock (versions)
            return [.. versions];
    }

    /// <summary>
    /// Returns a specific definition version.
    /// </summary>
    internal ResourceDefinition? GetDefinitionVersion(string definitionId, int version) =>
        GetDefinitionVersion(definitionId, version, TenantScope.Default);

    /// <summary>
    /// Returns a specific definition version in a tenant.
    /// </summary>
    internal ResourceDefinition? GetDefinitionVersion(string definitionId, int version, TenantScope tenantScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var tenant = TenantScopeResolver.Resolve(tenantScope);

        if (!definitions.TryGetValue((tenant.TenantId, definitionId), out var versions))
            return null;

        lock (versions)
            return versions.FirstOrDefault(definition => definition.Version == version);
    }

    /// <summary>
    /// Imports an exact definition version.
    /// </summary>
    internal void ImportDefinitionVersion(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var tenant = TenantScopeResolver.Resolve(definition.TenantScope);

        var versions = definitions.GetOrAdd((tenant.TenantId, definition.DefinitionId), _ => []);
        lock (versions)
        {
            var insertIndex = versions.FindIndex(existing => existing.Version >= definition.Version);
            if (insertIndex >= 0 && versions[insertIndex].Version == definition.Version)
                throw new InvalidOperationException($"Definition '{definition.DefinitionId}' version {definition.Version} already exists.");

            if (insertIndex < 0)
                versions.Add(definition);
            else
                versions.Insert(insertIndex, definition);
        }
    }

    /// <summary>
    /// Removes an imported definition version during rollback.
    /// </summary>
    internal void RemoveImportedDefinitionVersion(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var tenant = TenantScopeResolver.Resolve(definition.TenantScope);

        if (!definitions.TryGetValue((tenant.TenantId, definition.DefinitionId), out var versions))
            return;

        lock (versions)
            versions.RemoveAll(existing =>
                existing.Version == definition.Version
                && string.Equals(existing.Id, definition.Id, StringComparison.Ordinal));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Structured log methods (source generated)
    // ──────────────────────────────────────────────────────────────────────────

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Registered definition '{DefinitionId}' version {Version}.")]
    private partial void LogDefinitionRegistered(string definitionId, int version);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning,
        Message = "Definition '{DefinitionId}' was not found.")]
    private partial void LogDefinitionNotFound(string definitionId);
}
