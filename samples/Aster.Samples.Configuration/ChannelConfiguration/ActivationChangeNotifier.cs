using Aster.Core.Models.Lifecycle;

namespace Aster.Samples.Configuration.ChannelConfiguration;

/// <summary>
/// Fans activation changes out to the configuration providers that read from this Aster store.
/// Providers are created by configuration builders outside the Aster container, so they subscribe
/// here rather than being lifecycle hooks themselves.
/// </summary>
public sealed class ActivationChangeNotifier
{
    private readonly Lock gate = new();
    private List<Func<ResourceActivationLifecycleContext, CancellationToken, Task>> subscribers = [];

    /// <summary>Adds a subscriber; dispose the result to remove it.</summary>
    public IDisposable Subscribe(Func<ResourceActivationLifecycleContext, CancellationToken, Task> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (gate)
            subscribers = [.. subscribers, subscriber];

        return new Subscription(this, subscriber);
    }

    /// <summary>Invokes every subscriber in subscription order and waits for each one.</summary>
    public async Task NotifyAsync(ResourceActivationLifecycleContext change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        List<Func<ResourceActivationLifecycleContext, CancellationToken, Task>> snapshot;
        lock (gate)
            snapshot = subscribers;

        foreach (var subscriber in snapshot)
            await subscriber(change, cancellationToken);
    }

    private void Unsubscribe(Func<ResourceActivationLifecycleContext, CancellationToken, Task> subscriber)
    {
        lock (gate)
            subscribers = subscribers.Where(existing => existing != subscriber).ToList();
    }

    private sealed class Subscription(ActivationChangeNotifier owner, Func<ResourceActivationLifecycleContext, CancellationToken, Task> subscriber) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Unsubscribe(subscriber);
        }
    }
}
