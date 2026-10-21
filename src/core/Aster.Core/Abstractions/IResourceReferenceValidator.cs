using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Abstractions;

/// <summary>
/// Validates the targets of reference facet values in aspect payloads: each target must exist in
/// the tenant, match the facet's <see cref="FacetMetadata.TargetDefinitionIds"/> when declared, and
/// have a version active in the facet's <see cref="FacetMetadata.ReferenceResolution"/> channel when
/// one is declared. Resource saves run it after facet value validation; hosts can also call it to
/// preview the warnings a lenient save would produce.
/// </summary>
public interface IResourceReferenceValidator
{
    /// <summary>
    /// Validates every well-typed reference value in the given aspects with bounded reads: one
    /// latest-version read for all referenced resources, plus one active-version read per declared channel.
    /// </summary>
    /// <param name="definition">The definition that governs the payloads.</param>
    /// <param name="aspects">The aspect payloads keyed by attachment key.</param>
    /// <param name="tenantScope">The tenant to look targets up in; the default tenant when <see langword="null"/>.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The reference diagnostics, ordered like <see cref="Services.FacetValueValidator.Validate"/>;
    /// array elements carry <see cref="FacetValueDiagnostic.ElementIndex"/>. Empty when every target is valid.
    /// </returns>
    ValueTask<IReadOnlyList<FacetValueDiagnostic>> ValidateAsync(
        ResourceDefinition definition,
        IReadOnlyDictionary<string, object> aspects,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default);
}
