using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Aster.Samples.Configuration.ChannelConfiguration;

namespace Aster.Samples.Configuration.App;

/// <summary>
/// An application instance running in one environment. Its configuration comes from the Aster
/// channel of that environment; everything else is plain <c>Microsoft.Extensions</c> configuration
/// and options, so application code reads <see cref="IOptionsMonitor{TOptions}"/> as usual.
/// </summary>
public sealed class EnvironmentApplication : IAsyncDisposable
{
    private readonly IConfigurationRoot configuration;
    private readonly ServiceProvider services;

    private EnvironmentApplication(string environment, IConfigurationRoot configuration, ServiceProvider services)
    {
        Environment = environment;
        this.configuration = configuration;
        this.services = services;
    }

    /// <summary>The environment, and Aster channel, this instance runs in.</summary>
    public string Environment { get; }

    /// <summary>The application's configuration.</summary>
    public IConfiguration Configuration => configuration;

    /// <summary>The application's services.</summary>
    public IServiceProvider Services => services;

    /// <summary>The feature flag evaluator.</summary>
    public FeatureFlagEvaluator Flags => services.GetRequiredService<FeatureFlagEvaluator>();

    /// <summary>The checkout options, which follow the configuration as it reloads.</summary>
    public IOptionsMonitor<CheckoutOptions> Checkout => services.GetRequiredService<IOptionsMonitor<CheckoutOptions>>();

    /// <summary>Starts an application instance in <paramref name="environment"/>.</summary>
    /// <param name="asterServices">The service provider of the Aster configuration store.</param>
    /// <param name="environment">The environment, which is the Aster channel to read.</param>
    public static EnvironmentApplication Start(IServiceProvider asterServices, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddAsterChannel(asterServices, environment)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<FeatureFlagsOptions>(configuration.GetSection(ChannelConfigurationLoader.FeatureFlagsSection));
        services.Configure<CheckoutOptions>(configuration.GetSection(CheckoutOptions.Section));
        services.AddSingleton<FeatureFlagEvaluator>();

        return new EnvironmentApplication(environment, configuration, services.BuildServiceProvider());
    }

    /// <summary>Stops the instance; its configuration provider stops listening for activations.</summary>
    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        (configuration as IDisposable)?.Dispose();
    }
}
