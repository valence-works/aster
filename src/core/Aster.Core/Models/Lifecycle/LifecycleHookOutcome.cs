namespace Aster.Core.Models.Lifecycle;

/// <summary>
/// Result returned by a lifecycle hook.
/// </summary>
public sealed record LifecycleHookOutcome
{
    /// <summary>
    /// Outcome status.
    /// </summary>
    public required LifecycleHookOutcomeStatus Status { get; init; }

    /// <summary>
    /// Stable machine-readable code for rejected or failed outcomes.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Human-readable explanation for rejected or failed outcomes.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Optional structured diagnostics.
    /// </summary>
    public IReadOnlyList<LifecycleHookDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>
    /// Creates a continue outcome.
    /// </summary>
    public static LifecycleHookOutcome Continue() =>
        new() { Status = LifecycleHookOutcomeStatus.Continue };

    /// <summary>
    /// Creates a rejected outcome.
    /// </summary>
    public static LifecycleHookOutcome Reject(
        string code,
        string message,
        IReadOnlyList<LifecycleHookDiagnostic>? diagnostics = null) =>
        new()
        {
            Status = LifecycleHookOutcomeStatus.Rejected,
            Code = code,
            Message = message,
            Diagnostics = diagnostics ?? [],
        };

    /// <summary>
    /// Creates a failed outcome.
    /// </summary>
    public static LifecycleHookOutcome Fail(
        string code,
        string message,
        IReadOnlyList<LifecycleHookDiagnostic>? diagnostics = null) =>
        new()
        {
            Status = LifecycleHookOutcomeStatus.Failed,
            Code = code,
            Message = message,
            Diagnostics = diagnostics ?? [],
        };
}
