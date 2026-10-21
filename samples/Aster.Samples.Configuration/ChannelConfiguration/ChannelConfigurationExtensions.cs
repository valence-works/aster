using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Aster.Core.Extensions;

namespace Aster.Samples.Configuration.ChannelConfiguration;

/// <summary>
/// Registration helpers for the configuration store and the applications that read from it.
/// </summary>
public static class ChannelConfigurationExtensions
{
    /// <summary>
    /// Adds what the Aster container needs to serve channel configuration: the loader, the change
    /// notifier and the lifecycle hook that drives reloads. Call it next to <c>AddAsterCore()</c>.
    /// </summary>
    public static IServiceCollection AddChannelConfigurationStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ActivationChangeNotifier>();
        services.AddSingleton<ChannelConfigurationLoader>();
        services.AddResourceLifecycleHook<ConfigurationReloadHook>();
        return services;
    }

    /// <summary>
    /// Adds the configuration active in <paramref name="channel"/> of the Aster store in
    /// <paramref name="asterServices"/>, reloaded whenever that channel's activations change.
    /// </summary>
    public static IConfigurationBuilder AddAsterChannel(this IConfigurationBuilder builder, IServiceProvider asterServices, string channel)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(asterServices);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        return builder.Add(new AsterChannelConfigurationSource(asterServices, channel));
    }
}
