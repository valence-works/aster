using Aster.Elsa.Features;
using Elsa.Features.Services;

namespace Aster.Elsa;

/// <summary>
/// Enables the Aster module in Elsa's classic feature system.
/// </summary>
public static class AsterModuleExtensions
{
    /// <summary>
    /// Enables <see cref="AsterFeature"/>: the Aster activities, with Elsa's current tenant mapped to an Aster tenant
    /// scope on every call. Register Aster itself (<c>AddAsterCore()</c> and a provider) on the host's service
    /// collection; this call doesn't choose a provider.
    /// </summary>
    /// <param name="module">The Elsa module being configured in <c>AddElsa</c>.</param>
    /// <param name="configure">Configures the feature.</param>
    /// <returns>The <paramref name="module"/> for chaining.</returns>
    public static IModule UseAster(this IModule module, Action<AsterFeature>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        module.Configure(configure);
        return module;
    }
}
