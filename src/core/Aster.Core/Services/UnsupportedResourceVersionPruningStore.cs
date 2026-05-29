using Aster.Core.Abstractions;
using Aster.Core.Models.Tenancy;

namespace Aster.Core.Services;

/// <summary>
/// Fallback pruning store used when the active resource version provider does not support destructive pruning.
/// </summary>
public sealed class UnsupportedResourceVersionPruningStore : IResourceVersionPruningStore
{
    /// <inheritdoc />
    public ValueTask<bool> PruneVersionAsync(
        string resourceId,
        int resourceVersion,
        TenantScope tenantScope,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The active resource version provider does not support destructive version pruning.");
}
