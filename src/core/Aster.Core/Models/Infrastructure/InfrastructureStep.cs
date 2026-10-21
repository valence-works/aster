namespace Aster.Core.Models.Infrastructure;

/// <summary>
/// One of a provider's infrastructure steps.
/// </summary>
/// <param name="Id">The stable, ordered step ID, such as <c>0001-initial-schema</c>.</param>
/// <param name="Description">What the step creates or changes.</param>
public sealed record InfrastructureStep(string Id, string Description);

/// <summary>
/// A step recorded as applied.
/// </summary>
/// <param name="Id">The step ID.</param>
/// <param name="AppliedAt">When it was applied.</param>
public sealed record AppliedInfrastructureStep(string Id, DateTimeOffset AppliedAt);

/// <summary>
/// The result of <c>IInfrastructureRunner.VerifyAsync</c>.
/// </summary>
/// <param name="PendingSteps">Steps not yet applied.</param>
/// <param name="UnknownAppliedStepIds">Applied steps this version of the provider doesn't define.</param>
public sealed record InfrastructureVerification(
    IReadOnlyList<InfrastructureStep> PendingSteps,
    IReadOnlyList<string> UnknownAppliedStepIds)
{
    /// <summary>
    /// Gets whether the database has exactly this provider's steps applied.
    /// </summary>
    public bool IsCurrent => PendingSteps.Count == 0 && UnknownAppliedStepIds.Count == 0;
}
