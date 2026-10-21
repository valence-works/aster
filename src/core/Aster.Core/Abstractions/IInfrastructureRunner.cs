using Aster.Core.Models.Infrastructure;

namespace Aster.Core.Abstractions;

/// <summary>
/// Applies a persistence provider's infrastructure steps (tables, indexes, ...) in order and records which have
/// run. Each provider owns its steps and where it records them; this contract only lets a host inspect and apply
/// them, at startup or from a deployment step.
/// </summary>
public interface IInfrastructureRunner
{
    /// <summary>
    /// Applies every pending step in order. Safe to call from several processes at once: each step runs once.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The steps this call applied; empty when the database was already current.</returns>
    ValueTask<IReadOnlyList<InfrastructureStep>> ApplyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's steps that have not been applied, in the order they would run. Read-only.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The pending steps.</returns>
    ValueTask<IReadOnlyList<InfrastructureStep>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the steps recorded as applied, in the order they were applied. Read-only.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The applied steps.</returns>
    ValueTask<IReadOnlyList<AppliedInfrastructureStep>> GetAppliedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Compares the recorded steps with the provider's: pending steps, and applied steps this version of the
    /// provider doesn't know (the database was upgraded by a newer one). Read-only.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The verification result.</returns>
    ValueTask<InfrastructureVerification> VerifyAsync(CancellationToken cancellationToken = default);
}
