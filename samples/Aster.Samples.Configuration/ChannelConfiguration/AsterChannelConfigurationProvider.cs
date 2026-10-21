using Microsoft.Extensions.Configuration;
using Aster.Core.Models.Lifecycle;

namespace Aster.Samples.Configuration.ChannelConfiguration;

/// <summary>
/// An <see cref="IConfigurationProvider"/> over the configuration active in one Aster channel. It
/// reloads, and raises its change token, whenever a version is activated or deactivated in that
/// channel, so <c>IOptionsMonitor&lt;T&gt;</c> and <c>IConfiguration.GetReloadToken()</c> consumers see
/// promotions and rollbacks without a restart.
/// </summary>
public sealed class AsterChannelConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly ChannelConfigurationLoader loader;
    private readonly IDisposable subscription;
    private readonly SemaphoreSlim reloadLock = new(1, 1);

    /// <summary>Creates the provider and subscribes it to activation changes.</summary>
    public AsterChannelConfigurationProvider(ChannelConfigurationLoader loader, ActivationChangeNotifier notifier, string channel)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        this.loader = loader;
        Channel = channel;
        subscription = notifier.Subscribe(OnActivationChangedAsync);
    }

    /// <summary>The channel, that is the environment, this application reads its configuration from.</summary>
    public string Channel { get; }

    /// <summary>
    /// The error of the last failed reload, or <see langword="null"/> once a reload succeeds. A failed
    /// reload keeps the last good configuration rather than failing the activation that caused it.
    /// </summary>
    public Exception? LastReloadError { get; private set; }

    /// <summary>
    /// Loads synchronously, because <see cref="IConfigurationBuilder.Build"/> does. Hooks reload
    /// through <see cref="ReloadAsync"/>.
    /// </summary>
    public override void Load() => Data = loader.LoadAsync(Channel).GetAwaiter().GetResult();

    /// <summary>Reads the channel again and raises the change token.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        // Serialized so that a slower, older load cannot overwrite a newer one.
        await reloadLock.WaitAsync(cancellationToken);
        try
        {
            Data = await loader.LoadAsync(Channel, cancellationToken);
            LastReloadError = null;
        }
        finally
        {
            reloadLock.Release();
        }

        OnReload();
    }

    private async Task OnActivationChangedAsync(ResourceActivationLifecycleContext change, CancellationToken cancellationToken)
    {
        if (!string.Equals(change.Channel, Channel, StringComparison.Ordinal))
            return;

        try
        {
            await ReloadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastReloadError = exception;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        subscription.Dispose();
        reloadLock.Dispose();
    }
}
