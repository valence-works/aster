using Aster.Core.Models.Policies;

namespace Aster.Core.Abstractions;

/// <summary>
/// Evaluates policy declarations and returns non-mutating preview results.
/// </summary>
public interface IResourcePolicyEvaluationService
{
    /// <summary>
    /// Previews candidate policy outcomes without applying writes.
    /// </summary>
    /// <param name="request">Preview request.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Preview result.</returns>
    ValueTask<ResourcePolicyEvaluationPreview> PreviewAsync(
        ResourcePolicyEvaluationRequest request,
        CancellationToken cancellationToken = default);
}
