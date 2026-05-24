using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Core.Services;

/// <summary>
/// Default deterministic lifecycle hook dispatcher.
/// </summary>
public sealed class ResourceLifecycleHookDispatcher : IResourceLifecycleHookDispatcher
{
    private readonly IServiceScopeFactory scopeFactory;

    /// <summary>
    /// Initializes a new instance of <see cref="ResourceLifecycleHookDispatcher"/>.
    /// </summary>
    public ResourceLifecycleHookDispatcher(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        this.scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public ValueTask InvokeBeforeSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeBeforeAsync(context, static (hook, ctx, ct) => hook.OnBeforeSaveAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeAfterSaveAsync(ResourceSaveLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeAfterAsync(context, static (hook, ctx, ct) => hook.OnAfterSaveAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeBeforeActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeBeforeAsync(context, static (hook, ctx, ct) => hook.OnBeforeActivateAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeAfterAsync(context, static (hook, ctx, ct) => hook.OnAfterActivateAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeBeforeDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeBeforeAsync(context, static (hook, ctx, ct) => hook.OnBeforeDeactivateAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeAfterDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeAfterAsync(context, static (hook, ctx, ct) => hook.OnAfterDeactivateAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeBeforeExportAsync(ResourceExportLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeBeforeAsync(context, static (hook, ctx, ct) => hook.OnBeforeExportAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeAfterExportAsync(ResourceExportLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeAfterAsync(context, static (hook, ctx, ct) => hook.OnAfterExportAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeBeforePreviewImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeBeforeAsync(context, static (hook, ctx, ct) => hook.OnBeforePreviewImportAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeAfterPreviewImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeAfterAsync(context, static (hook, ctx, ct) => hook.OnAfterPreviewImportAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeBeforeImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeBeforeAsync(context, static (hook, ctx, ct) => hook.OnBeforeImportAsync(ctx, ct), cancellationToken);

    /// <inheritdoc />
    public ValueTask InvokeAfterImportAsync(ResourceImportLifecycleContext context, CancellationToken cancellationToken = default) =>
        InvokeAfterAsync(context, static (hook, ctx, ct) => hook.OnAfterImportAsync(ctx, ct), cancellationToken);

    private async ValueTask InvokeBeforeAsync<TContext>(
        TContext context,
        Func<IResourceLifecycleHook, TContext, CancellationToken, ValueTask<LifecycleHookOutcome>> invoke,
        CancellationToken cancellationToken)
        where TContext : ResourceLifecycleContext
    {
        ArgumentNullException.ThrowIfNull(context);

        using var scope = scopeFactory.CreateScope();
        foreach (var hook in scope.ServiceProvider.GetServices<IResourceLifecycleHook>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.CancellationToken.ThrowIfCancellationRequested();
            var hookContext = ResourceLifecycleHookContextSnapshots.Snapshot(context);

            LifecycleHookOutcome outcome;
            try
            {
                outcome = await invoke(hook, hookContext, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Failed(context.LifecyclePoint, hook.GetType(), exception);
            }

            ValidateOutcome(outcome, context.LifecyclePoint, hook.GetType());
        }
    }

    private async ValueTask InvokeAfterAsync<TContext>(
        TContext context,
        Func<IResourceLifecycleHook, TContext, CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken)
        where TContext : ResourceLifecycleContext
    {
        ArgumentNullException.ThrowIfNull(context);

        using var scope = scopeFactory.CreateScope();
        foreach (var hook in scope.ServiceProvider.GetServices<IResourceLifecycleHook>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.CancellationToken.ThrowIfCancellationRequested();
            var hookContext = ResourceLifecycleHookContextSnapshots.Snapshot(context);

            try
            {
                await invoke(hook, hookContext, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw Failed(context.LifecyclePoint, hook.GetType(), exception);
            }
        }
    }

    private static void ValidateOutcome(
        LifecycleHookOutcome? outcome,
        LifecyclePoint lifecyclePoint,
        Type hookType)
    {
        if (outcome is null)
        {
            throw Failed(
                lifecyclePoint,
                hookType,
                $"Lifecycle hook '{hookType.FullName}' returned a null outcome at '{lifecyclePoint}'.",
                []);
        }

        switch (outcome.Status)
        {
            case LifecycleHookOutcomeStatus.Continue:
                return;

            case LifecycleHookOutcomeStatus.Rejected:
                throw new LifecycleHookException(
                    OutcomeCode(outcome, LifecycleHookException.RejectedCode),
                    HookMessage(outcome, lifecyclePoint, hookType, "rejected"),
                    lifecyclePoint,
                    hookType,
                    outcome.Diagnostics);

            case LifecycleHookOutcomeStatus.Failed:
                throw new LifecycleHookException(
                    OutcomeCode(outcome, LifecycleHookException.FailedCode),
                    HookMessage(outcome, lifecyclePoint, hookType, "failed"),
                    lifecyclePoint,
                    hookType,
                    outcome.Diagnostics);

            default:
                throw Failed(
                    lifecyclePoint,
                    hookType,
                    $"Lifecycle hook '{hookType.FullName}' returned unsupported outcome status '{outcome.Status}'.",
                    outcome.Diagnostics);
        }
    }

    private static LifecycleHookException Failed(LifecyclePoint lifecyclePoint, Type hookType, Exception exception) =>
        new(
            LifecycleHookException.FailedCode,
            $"Lifecycle hook '{hookType.FullName}' failed at '{lifecyclePoint}': {exception.Message}",
            lifecyclePoint,
            hookType,
            innerException: exception);

    private static LifecycleHookException Failed(
        LifecyclePoint lifecyclePoint,
        Type hookType,
        string message,
        IReadOnlyList<LifecycleHookDiagnostic> diagnostics) =>
        new(
            LifecycleHookException.FailedCode,
            message,
            lifecyclePoint,
            hookType,
            diagnostics);

    private static string HookMessage(
        LifecycleHookOutcome outcome,
        LifecyclePoint lifecyclePoint,
        Type hookType,
        string action)
    {
        if (!string.IsNullOrWhiteSpace(outcome.Message))
            return outcome.Message!;

        return $"Lifecycle hook '{hookType.FullName}' {action} at '{lifecyclePoint}'.";
    }

    private static string OutcomeCode(LifecycleHookOutcome outcome, string fallbackCode) =>
        string.IsNullOrWhiteSpace(outcome.Code) ? fallbackCode : outcome.Code!;
}
