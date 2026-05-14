using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register Aster Core in-memory services.
/// </summary>
public static class AsterCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Aster Core in-memory services:
    /// <see cref="InMemoryResourceDefinitionStore"/>, <see cref="InMemoryResourceManager"/>,
    /// <see cref="InMemoryQueryService"/>, <see cref="SystemTextJsonAspectBinder"/>,
    /// <see cref="SystemTextJsonFacetBinder"/>, and <see cref="GuidIdentityGenerator"/>.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAsterCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        // Identity
        services.AddSingleton<GuidIdentityGenerator>();
        services.AddSingleton<IIdentityGenerator>(sp => sp.GetRequiredService<GuidIdentityGenerator>());

        // Definition store
        services.AddSingleton<InMemoryResourceDefinitionStore>();
        services.AddSingleton<IResourceDefinitionStore>(sp => sp.GetRequiredService<InMemoryResourceDefinitionStore>());

        // Resource backing store
        services.AddSingleton<InMemoryResourceStore>();
        services.AddSingleton<IResourceVersionReader>(sp => sp.GetRequiredService<InMemoryResourceStore>());
        services.AddSingleton<IResourceVersionWriter>(sp => sp.GetRequiredService<InMemoryResourceStore>());

        // Resource manager
        services.AddSingleton<InMemoryResourceManager>();
        services.AddSingleton<DefaultResourceManager>();
        services.AddSingleton<IResourceManager>(sp => sp.GetRequiredService<DefaultResourceManager>());

        // Query service
        services.AddSingleton<InMemoryQueryService>();
        services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<InMemoryQueryService>());

        // Typed binders
        services.AddSingleton<SystemTextJsonAspectBinder>();
        services.AddSingleton<ITypedAspectBinder>(sp => sp.GetRequiredService<SystemTextJsonAspectBinder>());

        services.AddSingleton<SystemTextJsonFacetBinder>();
        services.AddSingleton<ITypedFacetBinder>(sp => sp.GetRequiredService<SystemTextJsonFacetBinder>());

        return services;
    }
}
