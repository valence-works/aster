using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Manages the registry of <see cref="ResourceDefinition"/> versions.
/// Each registration appends a new immutable version; existing versions are never overwritten.
/// </summary>
public interface IResourceDefinitionStore
{
    /// <summary>
    /// Returns the latest version of the definition, or <see langword="null"/> if not registered.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest version of the definition in the specified tenant, or <see langword="null"/> if not registered.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="tenantScope">Tenant scope for lookup.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<ResourceDefinition?> GetDefinitionAsync(string definitionId, TenantScope tenantScope, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a specific (<c>DefinitionId</c>, <c>Version</c>) snapshot, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="version">The specific version number to retrieve.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a specific tenant-scoped definition snapshot, or <see langword="null"/> if not found.
    /// </summary>
    /// <param name="definitionId">The logical definition identifier.</param>
    /// <param name="version">The specific version number to retrieve.</param>
    /// <param name="tenantScope">Tenant scope for lookup.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<ResourceDefinition?> GetDefinitionVersionAsync(string definitionId, int version, TenantScope tenantScope, CancellationToken cancellationToken);

    /// <summary>
    /// Always appends a new immutable version. Auto-increments <see cref="ResourceDefinition.Version"/>.
    /// Never overwrites an existing version.
    /// </summary>
    /// <param name="definition">The definition to register.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask RegisterDefinitionAsync(ResourceDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>
    /// Always appends a new immutable definition version inside the specified tenant.
    /// </summary>
    /// <param name="definition">The definition to register.</param>
    /// <param name="tenantScope">Tenant scope for registration.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask RegisterDefinitionAsync(ResourceDefinition definition, TenantScope tenantScope, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the latest version of each distinct definition ID.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest version of each distinct definition ID in the specified tenant.
    /// </summary>
    /// <param name="tenantScope">Tenant scope for listing.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    ValueTask<IEnumerable<ResourceDefinition>> ListDefinitionsAsync(TenantScope tenantScope, CancellationToken cancellationToken);
}
