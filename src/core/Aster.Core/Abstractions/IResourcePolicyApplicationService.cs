using Aster.Core.Models.Policies;

namespace Aster.Core.Abstractions;

/// <summary>
/// Applies selected policy preview outcomes through explicit host action.
/// </summary>
public interface IResourcePolicyApplicationService
{
    /// <summary>
    /// Applies supported archive and soft-delete policy candidates. Each candidate is re-checked before it is
    /// applied: its policy must still be declared with the submitted kind and outcome, its version must still be the
    /// latest, and that version must still match the policy's criteria (age at <c>AppliedAt</c>, activation state and
    /// lifecycle state) as the preview evaluates them. A candidate that fails a criterion fails with
    /// <c>policy-application-policy-mismatch</c> and a <c>criteria/...</c> path.
    /// </summary>
    /// <param name="request">Policy application request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Structured per-candidate application result.</returns>
    ValueTask<ResourcePolicyApplicationResult> ApplyAsync(
        ResourcePolicyApplicationRequest request,
        CancellationToken cancellationToken = default);
}
