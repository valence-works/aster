using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Samples.Configuration.ChannelConfiguration;

/// <summary>
/// Configuration source for <see cref="AsterChannelConfigurationProvider"/>. It takes the Aster
/// service provider because configuration is built before the application's own container.
/// </summary>
public sealed class AsterChannelConfigurationSource(IServiceProvider asterServices, string channel) : IConfigurationSource
{
    /// <inheritdoc />
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new AsterChannelConfigurationProvider(
        asterServices.GetRequiredService<ChannelConfigurationLoader>(),
        asterServices.GetRequiredService<ActivationChangeNotifier>(),
        channel);
}
