using System.Text.Json;
using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Persistence;

public abstract class TenantScopeStoreTests(IAsterTestDatabase database) : IDisposable
{
    protected IAsterTestDatabase Database { get; } = database;

    public void Dispose() => Database.Dispose();

    [SkippableFact]
    public async Task Store_AllowsSameDefinitionAndResourceIdsAcrossTenants()
    {
        await using var provider = Database.CreateProvider();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var reader = provider.GetRequiredService<IResourceVersionReader>();

        await definitions.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantA, CancellationToken.None);
        await definitions.RegisterDefinitionAsync(CreateDefinition(), TenantScopeTestFixtures.TenantB, CancellationToken.None);
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant A", TenantScopeTestFixtures.TenantA));
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("shared-product", "Tenant B", TenantScopeTestFixtures.TenantB));

        var tenantAResources = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
        })).ToList();
        var tenantBResources = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantB,
        })).ToList();

        Assert.Equal(1, (await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None))!.Version);
        Assert.Equal(1, (await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantB, CancellationToken.None))!.Version);
        Assert.Equal(TenantScopeTestFixtures.TenantA, tenantAResources.Single().TenantScope);
        Assert.Equal(TenantScopeTestFixtures.TenantB, tenantBResources.Single().TenantScope);
    }

    [SkippableFact]
    public async Task RegisterDefinitionAsync_WithoutTenantScope_AlwaysUsesDefaultTenant()
    {
        await using var provider = Database.CreateProvider();
        var definitions = provider.GetRequiredService<IResourceDefinitionStore>();
        var definition = CreateDefinition() with { TenantScope = TenantScopeTestFixtures.TenantA };

        await definitions.RegisterDefinitionAsync(definition);

        Assert.NotNull(await definitions.GetDefinitionAsync("Product"));
        Assert.Null(await definitions.GetDefinitionAsync("Product", TenantScopeTestFixtures.TenantA, CancellationToken.None));
    }

    [SkippableFact]
    public async Task UpdateActivationAsync_NormalizesPersistedPayloadIdentityToMethodArguments()
    {
        await using var provider = Database.CreateProvider();
        var writer = provider.GetRequiredService<IResourceVersionWriter>();
        var reader = provider.GetRequiredService<IResourceVersionReader>();
        await writer.SaveVersionAsync(TenantScopeTestFixtures.CreateResource("product-1", "Tenant A", TenantScopeTestFixtures.TenantA));

        var state = await writer.UpdateActivationAsync("product-1", "Published", new ActivationState
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ResourceId = "wrong-product",
            Channel = "Wrong",
            ActiveVersions = [1],
            LastUpdated = DateTime.UtcNow,
        });

        var active = (await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "Published",
        })).ToList();

        Assert.Equal("product-1", state.ResourceId);
        Assert.Equal("Published", state.Channel);
        Assert.Equal("product-1", active.Single().ResourceId);
    }

    private static Aster.Core.Models.Definitions.ResourceDefinition CreateDefinition() =>
        new ResourceDefinitionBuilder().WithDefinitionId("Product").Build();
}

public sealed class SqliteJsonTenantScopeTests() : TenantScopeStoreTests(new SqliteTestDatabase());

public sealed class PostgresJsonbTenantScopeTests() : TenantScopeStoreTests(new PostgresTestDatabase());
