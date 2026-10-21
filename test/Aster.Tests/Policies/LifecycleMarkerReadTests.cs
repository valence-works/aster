using Aster.Core.Abstractions;
using Aster.Core.Models.Instances;
using Aster.Tests.Persistence;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Policies;

/// <summary>
/// #189: hosts read a resource's marker (state, reason, marked-at) through
/// <see cref="IResourceLifecycleMarkerService.GetAsync"/>, tenant-scoped, on every provider.
/// </summary>
public sealed class LifecycleMarkerReadTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";
    private const string Postgres = "postgres";
    private const string SqlServer = "sqlserver";

    private static readonly DateTimeOffset MarkedAt = new(2026, 5, 25, 9, 30, 0, TimeSpan.Zero);

    private readonly SqliteTestDatabase sqlite = new();
    private readonly PostgresTestDatabase postgres = new();

    private readonly SqlServerTestDatabase sqlServer = new();

    public void Dispose()
    {
        sqlite.Dispose();
        postgres.Dispose();
        sqlServer.Dispose();
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    [InlineData(SqlServer)]
    public async Task GetAsync_MarkedResource_ReturnsStateReasonAndMarkedAt(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "product-1");
        var service = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        await service.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            ResourceId = "product-1",
            State = ResourceLifecycleMarkerState.SoftDeleted,
            MarkedAt = MarkedAt,
            Reason = "Discontinued",
        });

        var marker = await service.GetAsync("product-1");

        Assert.NotNull(marker);
        Assert.Equal("product-1", marker.ResourceId);
        Assert.Equal(ResourceLifecycleMarkerState.SoftDeleted, marker.State);
        Assert.Equal("Discontinued", marker.Reason);
        Assert.Equal(MarkedAt, marker.MarkedAt);
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    [InlineData(SqlServer)]
    public async Task GetAsync_UnmarkedOrUnknownResource_ReturnsNull(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider);
        await PolicyTestFixtures.SaveResourceAsync(provider, "unmarked");
        var service = provider.GetRequiredService<IResourceLifecycleMarkerService>();

        Assert.Null(await service.GetAsync("unmarked"));
        Assert.Null(await service.GetAsync("missing"));
    }

    [SkippableTheory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    [InlineData(SqlServer)]
    public async Task GetAsync_ReadsOnlyTheRequestedTenant(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantA);
        await PolicyTestFixtures.RegisterProductDefinitionAsync(provider, TenantScopeTestFixtures.TenantB);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", tenantScope: TenantScopeTestFixtures.TenantA);
        await PolicyTestFixtures.SaveResourceAsync(provider, "shared", tenantScope: TenantScopeTestFixtures.TenantB);
        var service = provider.GetRequiredService<IResourceLifecycleMarkerService>();
        await service.ApplyAsync(new ResourceLifecycleMarkerRequest
        {
            TenantScope = TenantScopeTestFixtures.TenantA,
            ResourceId = "shared",
            State = ResourceLifecycleMarkerState.Archived,
            MarkedAt = MarkedAt,
        });

        var tenantA = await service.GetAsync("shared", TenantScopeTestFixtures.TenantA);

        Assert.NotNull(tenantA);
        Assert.Equal(TenantScopeTestFixtures.TenantA, tenantA.TenantScope);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, tenantA.State);
        Assert.Null(await service.GetAsync("shared", TenantScopeTestFixtures.TenantB));
        Assert.Null(await service.GetAsync("shared"));
    }

    [SkippableTheory]
    [InlineData(Sqlite)]
    [InlineData(Postgres)]
    [InlineData(SqlServer)]
    public async Task GetAsync_ReadsTheStoredMarkerThroughAFreshProvider(string providerKind)
    {
        await using (var writer = CreateProvider(providerKind))
        {
            await PolicyTestFixtures.RegisterProductDefinitionAsync(writer);
            await PolicyTestFixtures.SaveResourceAsync(writer, "product-1");
            await writer.GetRequiredService<IResourceLifecycleMarkerService>().ApplyAsync(new ResourceLifecycleMarkerRequest
            {
                ResourceId = "product-1",
                State = ResourceLifecycleMarkerState.Archived,
                MarkedAt = MarkedAt,
                Reason = "Season ended",
            });
        }

        await using var reader = CreateProvider(providerKind);
        var marker = await reader.GetRequiredService<IResourceLifecycleMarkerService>().GetAsync("product-1");

        Assert.NotNull(marker);
        Assert.Equal(ResourceLifecycleMarkerState.Archived, marker.State);
        Assert.Equal("Season ended", marker.Reason);
        Assert.Equal(MarkedAt, marker.MarkedAt);
    }

    [Fact]
    public async Task GetAsync_BlankResourceId_Throws()
    {
        await using var provider = PolicyTestFixtures.CreateCoreProvider();
        var service = provider.GetRequiredService<IResourceLifecycleMarkerService>();

        await Assert.ThrowsAsync<ArgumentException>(async () => await service.GetAsync(" "));
    }

    private ServiceProvider CreateProvider(string providerKind) =>
        providerKind switch
        {
            Sqlite => sqlite.CreateProvider(),
            Postgres => postgres.CreateProvider(),
            SqlServer => sqlServer.CreateProvider(),
            _ => PolicyTestFixtures.CreateCoreProvider(),
        };
}
