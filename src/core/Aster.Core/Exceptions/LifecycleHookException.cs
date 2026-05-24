using Aster.Core.Models.Lifecycle;

namespace Aster.Core.Exceptions;

/// <summary>
/// Exception thrown when a lifecycle hook rejects or fails an operation.
/// </summary>
public sealed class LifecycleHookException : InvalidOperationException
{
    /// <summary>
    /// Stable code used when a before hook rejects an operation.
    /// </summary>
    public const string RejectedCode = "lifecycle-hook-rejected";

    /// <summary>
    /// Stable code used when a hook fails.
    /// </summary>
    public const string FailedCode = "lifecycle-hook-failed";

    /// <summary>
    /// Initializes a new instance of the <see cref="LifecycleHookException"/> class.
    /// </summary>
    public LifecycleHookException(
        string code,
        string message,
        LifecyclePoint lifecyclePoint,
        Type? hookType = null,
        IReadOnlyList<LifecycleHookDiagnostic>? diagnostics = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        LifecyclePoint = lifecyclePoint;
        HookType = hookType;
        Diagnostics = diagnostics ?? [];
    }

    /// <summary>
    /// Stable machine-readable failure code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Lifecycle point where the failure occurred.
    /// </summary>
    public LifecyclePoint LifecyclePoint { get; }

    /// <summary>
    /// Hook type that rejected or failed when available.
    /// </summary>
    public Type? HookType { get; }

    /// <summary>
    /// Structured diagnostics emitted by the hook.
    /// </summary>
    public IReadOnlyList<LifecycleHookDiagnostic> Diagnostics { get; }
}
