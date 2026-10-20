using Aster.Core.Models.Definitions;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Resolves reference facet values to target resource versions, following the facet's declared
/// <see cref="FacetMetadata.ReferenceResolution"/> and <see cref="FacetMetadata.TargetDefinitionIds"/>
/// (ADR 0002 §6). References are tenant-scoped: targets are looked up in the given tenant only.
/// </summary>
public interface IResourceReferenceResolver
{
    /// <summary>
    /// Resolves one reference value of a <c>reference</c> or <c>reference[]</c> facet.
    /// </summary>
    /// <param name="definition">The definition that declares the facet.</param>
    /// <param name="aspectKey">The aspect attachment key that holds the facet.</param>
    /// <param name="facetDefinitionId">The reference facet's ID.</param>
    /// <param name="targetResourceId">The referenced <c>ResourceId</c> (one element for <c>reference[]</c>).</param>
    /// <param name="tenantScope">The tenant to resolve in; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolution outcome.</returns>
    /// <exception cref="ArgumentException">The aspect or facet is not declared, or the facet is not a reference facet.</exception>
    ValueTask<ResourceReferenceResolution> ResolveAsync(
        ResourceDefinition definition,
        string aspectKey,
        string facetDefinitionId,
        string targetResourceId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);
}
