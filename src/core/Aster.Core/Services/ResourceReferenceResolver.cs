using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.References;
using Aster.Core.Models.Tenancy;
using static Aster.Core.Services.FacetMetadataValidator;

namespace Aster.Core.Services;

/// <summary>
/// Default <see cref="IResourceReferenceResolver"/>, built on <see cref="IResourceManager"/> reads so
/// every provider resolves identically.
/// </summary>
public sealed class ResourceReferenceResolver(IResourceManager resourceManager) : IResourceReferenceResolver
{
    private const string LatestResolution = "latest";

    /// <inheritdoc />
    public async ValueTask<ResourceReferenceResolution> ResolveAsync(
        ResourceDefinition definition,
        string aspectKey,
        string facetDefinitionId,
        string targetResourceId,
        TenantScope? tenantScope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(aspectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(facetDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);

        if (!definition.AspectDefinitions.TryGetValue(aspectKey, out var aspect))
            throw new ArgumentException($"Definition '{definition.DefinitionId}' has no aspect '{aspectKey}'.", nameof(aspectKey));

        var facet = aspect.FacetDefinitions.FirstOrDefault(candidate => string.Equals(candidate.FacetDefinitionId, facetDefinitionId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Aspect '{aspectKey}' has no facet '{facetDefinitionId}'.", nameof(facetDefinitionId));

        if (ParseFacetType(facet.Type).Element != FacetValueType.Reference)
            throw new ArgumentException($"Facet '{aspectKey}/{facetDefinitionId}' has type '{facet.Type}', not reference or reference[].", nameof(facetDefinitionId));

        var tenant = TenantScope.Resolve(tenantScope);
        var latest = await resourceManager.GetLatestVersionAsync(targetResourceId, tenant, cancellationToken);
        if (latest is null)
            return new(ResourceReferenceResolutionStatus.TargetNotFound, targetResourceId, null);

        if (facet.Metadata?.TargetDefinitionIds is { } targets && !targets.Contains(latest.DefinitionId, StringComparer.Ordinal))
            return new(ResourceReferenceResolutionStatus.TargetTypeMismatch, targetResourceId, latest);

        var channel = facet.Metadata?.ReferenceResolution;
        if (channel is null || string.Equals(channel, LatestResolution, StringComparison.OrdinalIgnoreCase))
            return new(ResourceReferenceResolutionStatus.Resolved, targetResourceId, latest);

        // Several active versions are possible only when activated with allowMultipleActive; take the newest.
        var active = (await resourceManager.GetActiveVersionsAsync(targetResourceId, channel, tenant, cancellationToken))
            .MaxBy(static version => version.Version);

        return active is null
            ? new(ResourceReferenceResolutionStatus.NotActiveInChannel, targetResourceId, null)
            : new(ResourceReferenceResolutionStatus.Resolved, targetResourceId, active);
    }
}
