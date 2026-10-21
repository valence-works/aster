using Aster.Core.Abstractions;
using Aster.Core.Models.Querying;
using Aster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Persistence.SqlServerJson;

/// <summary>
/// Extension methods for registering the SQL Server JSON provider.
/// </summary>
public static class SqlServerJsonAsterServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQL Server storage for definitions, shared aspects, resource versions, activation state, lifecycle
    /// markers, version pruning and portability, with JSON payloads in <c>nvarchar(max)</c>, and the query provider
    /// that translates <c>ResourceQuery</c> to SQL over <c>OPENJSON</c>. Call it after <c>AddAsterCore()</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the connection string, schema bootstrap and index projections.</param>
    /// <returns>The <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddAsterSqlServerJson(
        this IServiceCollection services,
        Action<SqlServerJsonAsterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SqlServerJsonAsterOptions();
        configure(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString, nameof(SqlServerJsonAsterOptions.ConnectionString));
        ValidateIndexProjections(options);

        services.AddSingleton(options);
        services.AddSingleton<SqlServerJsonInfrastructureRunner>();
        services.AddSingleton<IInfrastructureRunner>(sp => sp.GetRequiredService<SqlServerJsonInfrastructureRunner>());
        services.AddSingleton<SqlServerJsonResourceStore>();
        services.AddSingleton<SqlServerJsonAspectDefinitionStore>();
        services.AddSingleton<IAspectDefinitionStore>(sp => sp.GetRequiredService<SqlServerJsonAspectDefinitionStore>());
        services.AddSingleton<IResourceDefinitionStore>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourceVersionReader>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourceVersionWriter>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourceActivationStateReader>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourceVersionPruningStore>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourcePortabilityStore>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourceLifecycleMarkerStore>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<IResourceLifecycleMarkerClearStore>(sp => sp.GetRequiredService<SqlServerJsonResourceStore>());
        services.AddSingleton<SqlServerJsonQueryService>();
        services.AddSingleton<SqlServerJsonQueryProviderIdentity>();
        services.AddSingleton<SqlServerJsonQueryCapabilitiesProvider>();
        services.AddSingleton<IResourceQueryService>(sp => sp.GetRequiredService<SqlServerJsonQueryService>());
        services.AddSingleton<IResourceQueryProviderIdentity>(sp => sp.GetRequiredService<SqlServerJsonQueryProviderIdentity>());
        services.AddSingleton<IResourceQueryCapabilitiesProvider>(sp => sp.GetRequiredService<SqlServerJsonQueryCapabilitiesProvider>());

        return services;
    }

    // Fail at registration rather than at the first schema run: an index the provider can't build is a host error.
    private static void ValidateIndexProjections(SqlServerJsonAsterOptions options)
    {
        var validation = new IndexProjectionValidator().Validate(options.IndexProjections);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                "Invalid index projections: " + string.Join("; ", validation.Failures.Select(static failure => $"{failure.FieldName}: {failure.Code} ({failure.Message})")),
                nameof(options));
        }

        _ = SqlServerProjections.Plan(options.IndexProjections);
    }
}
