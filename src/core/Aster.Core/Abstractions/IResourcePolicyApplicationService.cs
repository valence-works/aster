using Aster.Core.Models.Policies;

namespace Aster.Core.Abstractions;

/// <summary>
/// Applies selected policy preview outcomes through explicit host action.
/// </summary>
public interface IResourcePolicyApplicationService
{
    /// <summary>
    /// Applies supported archive and soft-delete policy candidates.
    /// </summary>
    /// <param name="request">Policy application request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Structured per-candidate application result.</returns>
    ValueTask<ResourcePolicyApplicationResult> ApplyAsync(
        ResourcePolicyApplicationRequest request,
        CancellationToken cancellationToken = default);
}
