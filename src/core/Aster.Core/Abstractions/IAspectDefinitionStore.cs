using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Registry of shared <see cref="AspectDefinition"/> versions (ADR 0003). Each registration appends an
/// immutable version keyed by <c>(TenantScope, AspectDefinitionId, Version)</c>; existing versions are never
/// overwritten. Resource definitions pin a version by embedding its snapshot.
/// </summary>
public interface IAspectDefinitionStore
{
    /// <summary>
    /// Appends a new version of the aspect definition. The store assigns <see cref="AspectDefinition.Version"/>
    /// as the tenant's latest version plus one, and runs the facet metadata self-validation first.
    /// </summary>
    /// <param name="aspect">The aspect definition to register; its <c>Version</c> is ignored.</param>
    /// <param name="tenantScope">The tenant to register in; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The registered version, with its assigned <c>Version</c>.</returns>
    /// <exception cref="Exceptions.FacetMetadataValidationException">A facet's type or metadata is invalid.</exception>
    ValueTask<AspectDefinition> RegisterAsync(
        AspectDefinition aspect,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest version of an aspect definition, or <see langword="null"/> when none is registered.
    /// </summary>
    /// <param name="aspectDefinitionId">The logical aspect definition ID.</param>
    /// <param name="tenantScope">The tenant to read from; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<AspectDefinition?> GetLatestAsync(
        string aspectDefinitionId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one version of an aspect definition, or <see langword="null"/> when it does not exist.
    /// </summary>
    /// <param name="aspectDefinitionId">The logical aspect definition ID.</param>
    /// <param name="version">The version number.</param>
    /// <param name="tenantScope">The tenant to read from; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<AspectDefinition?> GetVersionAsync(
        string aspectDefinitionId,
        int version,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the latest version of every aspect definition in the tenant, ordered by
    /// <c>AspectDefinitionId</c> (ordinal).
    /// </summary>
    /// <param name="tenantScope">The tenant to read from; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<IReadOnlyList<AspectDefinition>> ListAsync(
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every version of an aspect definition, oldest first; empty when none is registered.
    /// </summary>
    /// <param name="aspectDefinitionId">The logical aspect definition ID.</param>
    /// <param name="tenantScope">The tenant to read from; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    ValueTask<IReadOnlyList<AspectDefinition>> ListVersionsAsync(
        string aspectDefinitionId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);
}
