using Aster.Core.Abstractions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Resolves the definition version that governs an update: the one the resource records in
/// <see cref="Resource.DefinitionVersion"/>, so registering a stricter definition version does not
/// change the rules for existing resources until they are explicitly upgraded (ADR 0002 addendum, #130).
/// </summary>
internal static class RecordedDefinitionResolver
{
    /// <summary>
    /// Returns the recorded definition version, or the latest version when the resource records
    /// none or the recorded version is no longer stored.
    /// </summary>
    public static async ValueTask<ResourceDefinition?> ResolveAsync(
        IResourceDefinitionStore definitionStore,
        Resource resource,
        TenantScope tenant,
        CancellationToken cancellationToken)
    {
        if (resource.DefinitionVersion is { } recordedVersion
            && await definitionStore.GetDefinitionVersionAsync(resource.DefinitionId, recordedVersion, tenant, cancellationToken) is { } recorded)
        {
            return recorded;
        }

        return await definitionStore.GetDefinitionAsync(resource.DefinitionId, tenant, cancellationToken);
    }
}
