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
    /// Registers a resource lifecycle hook in deterministic service registration order.
    /// </summary>
    /// <typeparam name="THook">The concrete lifecycle hook type.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddResourceLifecycleHook<THook>(this IServiceCollection services)
        where THook : class, IResourceLifecycleHook
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<THook>();
        services.AddSingleton<IResourceLifecycleHook>(sp => sp.GetRequiredService<THook>());

        return services;
    }

    /// <summary>
    /// Registers a custom resource query provider and its matching capability declaration.
    /// </summary>
    /// <typeparam name="TQueryService">The concrete query service type.</typeparam>
    /// <typeparam name="TCapabilitiesProvider">The concrete capability provider type.</typeparam>
    /// <param name="services">The service collection to add services to.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddResourceQueryProvider<TQueryService, TCapabilitiesProvider>(
        this IServiceCollection services)
        where TQueryService : class, IResourceQueryService, IResourceQueryProviderIdentity
        where TCapabilitiesProvider : class, IResourceQueryCapabilitiesProvider
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<TQueryService>();
        services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<TQueryService>());
        services.AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<TQueryService>());

        services.AddSingleton<TCapabilitiesProvider>();
        services.AddSingleton<IResourceQueryCapabilitiesProvider>(sp => sp.GetRequiredService<TCapabilitiesProvider>());

        return services;
    }

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
        services.AddSingleton<InMemoryPortabilityStore>();
        services.AddSingleton<IResourcePortabilityStore>(sp => sp.GetRequiredService<InMemoryPortabilityStore>());

        // Resource manager
        services.AddSingleton<InMemoryResourceManager>();
        services.AddSingleton<ResourceLifecycleHookDispatcher>();
        services.AddSingleton<IResourceLifecycleHookDispatcher>(sp => sp.GetRequiredService<ResourceLifecycleHookDispatcher>());
        services.AddSingleton<DefaultResourceManager>();
        services.AddSingleton<IResourceManager>(sp => sp.GetRequiredService<DefaultResourceManager>());
        services.AddSingleton<IResourceSchemaVersionService, ResourceSchemaVersionService>();
        services.AddSingleton<IResourcePortabilityService, ResourcePortabilityService>();

        // Query service
        services.AddSingleton<InMemoryQueryService>();
        services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<InMemoryQueryService>());
        services.AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<InMemoryQueryService>());
        services.AddSingleton<InMemoryQueryCapabilitiesProvider>();
        services.AddSingleton<IResourceQueryCapabilitiesProvider>(sp => sp.GetRequiredService<InMemoryQueryCapabilitiesProvider>());
        services.AddSingleton<IResourceQueryValidator, ResourceQueryValidator>();

        // Typed binders
        services.AddSingleton<SystemTextJsonAspectBinder>();
        services.AddSingleton<ITypedAspectBinder>(sp => sp.GetRequiredService<SystemTextJsonAspectBinder>());

        services.AddSingleton<SystemTextJsonFacetBinder>();
        services.AddSingleton<ITypedFacetBinder>(sp => sp.GetRequiredService<SystemTextJsonFacetBinder>());

        return services;
    }
}
