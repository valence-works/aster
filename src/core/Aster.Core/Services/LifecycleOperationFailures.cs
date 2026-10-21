using Aster.Core.Models.Lifecycle;
using Aster.Core.Models.Portability;

namespace Aster.Core.Services;

/// <summary>
/// Builds the <see cref="LifecycleOperationFailure"/> that after hooks receive when the underlying operation failed (#219).
/// </summary>
internal static class LifecycleOperationFailures
{
    public static LifecycleOperationFailure FromException(Exception exception) =>
        new()
        {
            Code = LifecycleOperationFailure.OperationFailedCode,
            Message = exception.Message,
            Exception = exception,
        };

    /// <summary>
    /// Uses the first error diagnostic, which is the one that failed a portability operation.
    /// </summary>
    public static LifecycleOperationFailure FromDiagnostics(
        IReadOnlyList<PortableDiagnostic> diagnostics,
        Exception? exception = null)
    {
        var error = diagnostics.FirstOrDefault(static diagnostic => diagnostic.Severity == PortableDiagnosticSeverity.Error);
        if (error is null)
        {
            return exception is null
                ? new() { Code = LifecycleOperationFailure.OperationFailedCode, Message = "The operation failed." }
                : FromException(exception);
        }

        return new()
        {
            Code = error.Code,
            Message = error.Message,
            Exception = exception,
        };
    }
}
