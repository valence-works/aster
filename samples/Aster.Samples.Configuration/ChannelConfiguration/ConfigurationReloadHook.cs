using Aster.Core.Abstractions;
using Aster.Core.Models.Lifecycle;
using Aster.Samples.Configuration.Model;

namespace Aster.Samples.Configuration.ChannelConfiguration;

/// <summary>
/// The lifecycle hook that turns an activation change into a configuration reload. It runs after
/// the activation state is written, so a reload reads the new state, and it finishes before
/// <see cref="IResourceManager.ActivateAsync(string, int, string, bool, CancellationToken)"/> returns.
/// </summary>
public sealed class ConfigurationReloadHook(ActivationChangeNotifier notifier) : ResourceLifecycleHook
{
    /// <inheritdoc />
    public override async ValueTask OnAfterActivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (IsConfiguration(context))
            await notifier.NotifyAsync(context, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask OnAfterDeactivateAsync(ResourceActivationLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (IsConfiguration(context))
            await notifier.NotifyAsync(context, cancellationToken);
    }

    // DefinitionId is null only when deactivating a resource without stored versions; reloading then is harmless.
    private static bool IsConfiguration(ResourceActivationLifecycleContext context) =>
        context.DefinitionId is null || ConfigurationDefinitions.All.Contains(context.DefinitionId);
}
