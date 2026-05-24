# Quickstart: Host Lifecycle Hooks

## Register Core Services And Hooks

```csharp
services
    .AddAsterCore()
    .AddResourceLifecycleHook<AuditLifecycleHook>()
    .AddResourceLifecycleHook<PublishPolicyHook>();
```

Hooks run in registration order for each lifecycle point. Existing behavior is unchanged when no hooks are registered.

## Observe Successful Saves

```csharp
public sealed class AuditLifecycleHook : ResourceLifecycleHook
{
    public override ValueTask OnAfterSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine(
            $"Saved {context.Resource!.ResourceId} v{context.Resource.Version} via {context.SaveKind}");

        return ValueTask.CompletedTask;
    }
}
```

After-save hooks run only after the resource version is successfully persisted. They observe the completed operation; they do not participate in rollback.

## Reject A Save Before Mutation

```csharp
public sealed class PublishPolicyHook : ResourceLifecycleHook
{
    public override ValueTask<LifecycleHookOutcome> OnBeforeSaveAsync(
        ResourceSaveLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.DefinitionId == "LockedProduct")
        {
            return ValueTask.FromResult(LifecycleHookOutcome.Reject(
                "locked-definition",
                "LockedProduct resources cannot be changed by this host."));
        }

        return ValueTask.FromResult(LifecycleHookOutcome.Continue());
    }
}
```

When a before hook rejects, later hooks for that lifecycle point do not run and the underlying save is not applied.

## Gate Activation

```csharp
public sealed class PublishedChannelHook : ResourceLifecycleHook
{
    public override ValueTask<LifecycleHookOutcome> OnBeforeActivateAsync(
        ResourceActivationLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Channel != "Published")
            return ValueTask.FromResult(LifecycleHookOutcome.Continue());

        return context.Version > 0
            ? ValueTask.FromResult(LifecycleHookOutcome.Continue())
            : ValueTask.FromResult(LifecycleHookOutcome.Reject(
                "invalid-published-version",
                "Only valid resource versions can be published."));
    }
}
```

Activation context includes the resource ID, version, channel, and whether multiple active versions were allowed.

## Observe Portability

```csharp
public sealed class PortabilityAuditHook : ResourceLifecycleHook
{
    public override ValueTask OnAfterImportAsync(
        ResourceImportLifecycleContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine(
            $"Import status: {context.ImportResult!.Status}; mapped {context.ImportResult.IdentityMap.Count} identities.");

        return ValueTask.CompletedTask;
    }
}
```

Portability hook rejections and failures surface through structured diagnostics where the portability operation already returns diagnostics.

## Expected Behavior

- Hooks are explicit: no scanning, attributes, or naming conventions.
- Before hooks can reject before mutation.
- After hooks observe successful operations only.
- Cancellation stops later hook invocation.
- Hook failures are visible to callers.
- Providers do not need storage-specific hook implementations.
