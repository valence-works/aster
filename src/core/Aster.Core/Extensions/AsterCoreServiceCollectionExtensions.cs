using Aster.Core.Abstractions;
using Aster.Core.InMemory;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

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

        // Identity
        services.AddSingleton<GuidIdentityGenerator>();
        services.AddSingleton<IIdentityGenerator>(sp => sp.GetRequiredService<GuidIdentityGenerator>());

        // Definition store
        services.AddSingleton<InMemoryResourceDefinitionStore>();
        services.AddSingleton<IResourceDefinitionStore>(sp => sp.GetRequiredService<InMemoryResourceDefinitionStore>());

        // Resource backing store
        services.AddSingleton<InMemoryResourceStore>();

        // Resource manager
        services.AddSingleton<InMemoryResourceManager>();
        services.AddSingleton<IResourceManager>(sp => sp.GetRequiredService<InMemoryResourceManager>());
        services.AddSingleton<IResourceWriteStore>(sp => sp.GetRequiredService<InMemoryResourceManager>());

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
