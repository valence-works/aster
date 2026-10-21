namespace Aster.Core.Models.Lifecycle;

/// <summary>
/// Why the underlying operation failed, as seen by after hooks when
/// <see cref="ResourceLifecycleContext.OperationStatus"/> is <see cref="LifecycleOperationStatus.Failed"/>.
/// </summary>
public sealed record LifecycleOperationFailure
{
    /// <summary>
    /// Code for a save, activation, or deactivation whose store write threw.
    /// </summary>
    public const string OperationFailedCode = "lifecycle-operation-failed";

    /// <summary>
    /// Stable machine-readable code. Save, activation, and deactivation use <see cref="OperationFailedCode"/>;
    /// export, import preview, and import use the code of the error diagnostic that failed the operation.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable explanation.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Exception that failed the operation, or <see langword="null"/> when it failed with diagnostics only,
    /// such as an import whose plan has error diagnostics. For save, activation, and deactivation this is the
    /// exception the caller receives.
    /// </summary>
    public Exception? Exception { get; init; }
}
