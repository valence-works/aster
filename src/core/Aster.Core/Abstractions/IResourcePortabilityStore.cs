using Aster.Core.Models.Portability;

namespace Aster.Core.Abstractions;

/// <summary>
/// Provider-facing storage contract used by portability orchestration.
/// </summary>
public interface IResourcePortabilityStore
{
    /// <summary>
    /// Reads the definitions, resources, and activation state needed for an export request.
    /// </summary>
    ValueTask<PortableStoreSnapshot> ReadSnapshotAsync(
        PortableStoreReadRequest request,
        CancellationToken cancellationToken = default);
}
