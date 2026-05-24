namespace Aster.Core.Models.Lifecycle;

/// <summary>
/// Structured diagnostic emitted by a lifecycle hook.
/// </summary>
public sealed record LifecycleHookDiagnostic
{
    /// <summary>
    /// Stable machine-readable diagnostic code.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable diagnostic detail.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Lifecycle point associated with the diagnostic.
    /// </summary>
    public required LifecyclePoint LifecyclePoint { get; init; }

    /// <summary>
    /// Optional hook type that emitted the diagnostic.
    /// </summary>
    public string? HookType { get; init; }
}
