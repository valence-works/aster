using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Extensions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Tenancy;

public static class TenantScopeTestFixtures
{
    public static TenantScope TenantA { get; } = TenantScope.FromTenantId("tenant-a");

    public static TenantScope TenantB { get; } = TenantScope.FromTenantId("tenant-b");

    public static ServiceProvider CreateCoreProvider() =>
        new ServiceCollection()
            .AddAsterCore()
            .BuildServiceProvider();

    public static ServiceProvider CreateSqliteProvider(string databasePath) =>
        new ServiceCollection()
            .AddAsterCore()
            .AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}")
            .BuildServiceProvider();

    public static async Task RegisterProductDefinitionAsync(
        IServiceProvider provider,
        TenantScope? tenantScope = null)
    {
        var store = provider.GetRequiredService<IResourceDefinitionStore>();
        var definition = new ResourceDefinitionBuilder()
            .WithDefinitionId("Product")
            .Build();

        if (tenantScope is null)
            await store.RegisterDefinitionAsync(definition);
        else
            await store.RegisterDefinitionAsync(definition, tenantScope, CancellationToken.None);
    }

    public static async Task<Resource> CreateProductAsync(
        IServiceProvider provider,
        string resourceId,
        string title,
        TenantScope? tenantScope = null)
    {
        var manager = provider.GetRequiredService<IResourceManager>();
        return await manager.CreateAsync("Product", new CreateResourceRequest
        {
            ResourceId = resourceId,
            TenantScope = tenantScope,
            InitialAspects =
            {
                ["Title"] = new TitleAspect(title),
            },
        });
    }

    public static Resource CreateResource(
        string resourceId,
        string title,
        TenantScope? tenantScope = null,
        int version = 1) =>
        new()
        {
            TenantScope = tenantScope ?? TenantScope.Default,
            ResourceId = resourceId,
            Id = $"{resourceId}-{version}",
            DefinitionId = "Product",
            DefinitionVersion = 1,
            Version = version,
            Created = DateTime.UtcNow.AddMinutes(version),
            Aspects = new Dictionary<string, object>
            {
                ["Title"] = new TitleAspect(title),
            },
        };

    public static ResourceQuery ProductQuery(TenantScope? tenantScope = null) =>
        new()
        {
            TenantScope = tenantScope,
            DefinitionId = "Product",
            Sorts = [new SortExpression("ResourceId")],
        };

    public sealed record TitleAspect(string Title);
}
