using Aster.Elsa.Studio.Client;
using Aster.Elsa.Studio.Module;
using Elsa.Studio.Contracts;
using Elsa.Studio.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Elsa.Studio;

/// <summary>
/// Registers the Aster module in an Elsa Studio host.
/// </summary>
public static class AsterStudioServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Aster module to Elsa Studio: the Resources menu entry, the definition list, resource lists and
    /// editors, version history with channel activation, and the reference picker editor. The module talks to the
    /// <c>Aster.AspNetCore</c> endpoints at <paramref name="apiBaseAddress"/>, the prefix the server passed to
    /// <c>MapAster</c> (for example <c>https://server/api/aster</c>). Forms render through Elsa Studio's UI-hint
    /// editors, so the host must also register Elsa Studio's default UI-hint handlers, as its workflows module does.
    /// </summary>
    /// <param name="services">The Elsa Studio host's services.</param>
    /// <param name="apiBaseAddress">The absolute base address of the Aster endpoints.</param>
    /// <returns>
    /// The builder of the module's <see cref="HttpClient"/>, so the host can add its authentication handler, for
    /// example <c>.AddHttpMessageHandler&lt;AuthenticatingApiHttpMessageHandler&gt;()</c>.
    /// </returns>
    public static IHttpClientBuilder AddAsterStudioModule(this IServiceCollection services, Uri apiBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(apiBaseAddress);
        if (!apiBaseAddress.IsAbsoluteUri)
            throw new ArgumentException("The Aster API base address must be absolute.", nameof(apiBaseAddress));

        // Relative request URIs resolve under the prefix only when the base address ends with a slash.
        var baseAddress = apiBaseAddress.AbsoluteUri.EndsWith('/') ? apiBaseAddress : new Uri(apiBaseAddress.AbsoluteUri + "/");

        services.AddScoped<IFeature, AsterStudioFeature>();
        services.AddScoped<IMenuProvider, AsterStudioMenu>();
        services.AddUIHintHandler<AsterReferencePickerHandler>();
        return services.AddHttpClient<AsterStudioClient>(client => client.BaseAddress = baseAddress);
    }
}
