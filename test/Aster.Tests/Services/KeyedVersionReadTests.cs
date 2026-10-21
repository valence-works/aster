using Aster.Core.Abstractions;
using Aster.Core.Exceptions;
using Aster.Core.Models.Instances;
using Aster.Core.Models.Querying;
using Aster.Core.Models.Tenancy;
using Aster.Core.Services;
using Aster.Tests.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aster.Tests.Services;

/// <summary>
/// #166: single-version operations address one row (latest or a version number) instead of reading the
/// resource's history, with the same results on both providers.
/// </summary>
public sealed class KeyedVersionReadTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-keyed-{Guid.NewGuid():N}.db");

    public static TheoryData<string> Providers => new() { "InMemory", "Sqlite" };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SingleVersionReads_ReturnTheAddressedVersion(string provider)
    {
        await using var services = await SeedAsync(provider);
        var manager = services.GetRequiredService<IResourceManager>();

        Assert.Equal(3, (await manager.GetLatestVersionAsync("a1"))!.Version);
        Assert.Equal(2, (await manager.GetVersionAsync("a1", 2))!.Version);
        Assert.Equal("Product", (await manager.GetVersionAsync("a1", 2))!.DefinitionId);
        Assert.Null(await manager.GetVersionAsync("a1", 9));
        Assert.Null(await manager.GetVersionAsync("missing", 1));
        Assert.Null(await manager.GetLatestVersionAsync("missing"));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task SingleVersionReads_StayInTheirTenant(string provider)
    {
        await using var services = await SeedAsync(provider);
        var manager = services.GetRequiredService<IResourceManager>();
        var tenantB = TenantScopeTestFixtures.TenantB;

        Assert.Equal(1, (await manager.GetLatestVersionAsync("a1", tenantB, CancellationToken.None))!.Version);
        Assert.Null(await manager.GetVersionAsync("a1", 3, tenantB, CancellationToken.None));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PrunedVersion_IsNotFoundAndCannotBeActivated(string provider)
    {
        await using var services = await SeedAsync(provider);
        var manager = services.GetRequiredService<IResourceManager>();
        Assert.True(await services.GetRequiredService<IResourceVersionPruningStore>()
            .PruneVersionAsync("a1", 2, TenantScope.Default));

        Assert.Null(await manager.GetVersionAsync("a1", 2));
        await Assert.ThrowsAsync<VersionNotFoundException>(async () => await manager.ActivateAsync("a1", 2, "published"));

        await manager.ActivateAsync("a1", 1, "published");
        await manager.ActivateAsync("a1", 3, "published", allowMultipleActive: true);
        Assert.Equal(new[] { 1, 3 }, (await manager.GetActiveVersionsAsync("a1", "published")).Select(static r => r.Version));

        await manager.DeactivateAsync("a1", 1, "published");
        Assert.Equal(new[] { 3 }, (await manager.GetActiveVersionsAsync("a1", "published")).Select(static r => r.Version));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Reader_VersionFilter_AppliesWithinTheScope(string provider)
    {
        await using var services = await SeedAsync(provider);
        var reader = services.GetRequiredService<IResourceVersionReader>();

        var firstVersions = await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.AllVersions,
            ResourceIds = ["a2", "a1", "missing"],
            Version = 1,
        });
        Assert.Equal(new[] { ("a1", 1), ("a2", 1) }, Keys(firstVersions));

        // a1's latest is version 3, so only a2 matches "latest and version 1".
        var latestFirst = await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Latest,
            ResourceIds = ["a1", "a2"],
            Version = 1,
        });
        Assert.Equal(new[] { "a2" }, latestFirst.Select(static r => r.ResourceId));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Reader_ActiveScope_ReturnsActiveVersionsInOrder(string provider)
    {
        await using var services = await SeedAsync(provider);
        var manager = services.GetRequiredService<IResourceManager>();
        await manager.ActivateAsync("a2", 1, "published");
        await manager.ActivateAsync("a1", 3, "published");
        await manager.ActivateAsync("a1", 1, "published", allowMultipleActive: true);
        await manager.ActivateAsync("a1", 2, "preview");

        var reader = services.GetRequiredService<IResourceVersionReader>();
        var tenantWide = await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "published",
        });
        Assert.Equal(new[] { ("a1", 1), ("a1", 3), ("a2", 1) }, Keys(tenantWide));

        var bounded = await reader.ReadVersionsAsync(new ResourceVersionReadRequest
        {
            Scope = ResourceVersionScope.Active,
            ActivationChannel = "preview",
            ResourceIds = ["a1", "a2"],
        });
        Assert.Equal(new[] { ("a1", 2) }, bounded.Select(static r => (r.ResourceId, r.Version)));
    }

    [Fact]
    public async Task SingleVersionOperations_NeverReadAWholeHistory()
    {
        await using var services = await SeedAsync("InMemory");
        var reader = new RecordingReader(services.GetRequiredService<IResourceVersionReader>());
        var manager = new DefaultResourceManager(
            services.GetRequiredService<IResourceDefinitionStore>(),
            reader,
            services.GetRequiredService<IResourceVersionWriter>(),
            services.GetRequiredService<IIdentityGenerator>(),
            NullLogger<DefaultResourceManager>.Instance);

        await manager.UpdateAsync("a1", new UpdateResourceRequest { BaseVersion = 3 });
        await manager.ActivateAsync("a1", 2, "published");
        await manager.DeactivateAsync("a1", 2, "published");
        await manager.GetLatestVersionAsync("a1");
        await manager.GetVersionAsync("a1", 1);

        Assert.NotEmpty(reader.Requests);
        Assert.DoesNotContain(reader.Requests, static request =>
            request.Scope is ResourceVersionScope.AllVersions or ResourceVersionScope.Draft && request.Version is null);
    }

    // Ordering across resources is provider-defined, so compare sorted keys.
    private static IEnumerable<(string, int)> Keys(IEnumerable<Resource> resources) =>
        resources
            .Select(static r => (r.ResourceId, r.Version))
            .OrderBy(static key => key.ResourceId, StringComparer.Ordinal)
            .ThenBy(static key => key.Version);

    private async Task<ServiceProvider> SeedAsync(string provider)
    {
        var services = provider == "Sqlite"
            ? TenantScopeTestFixtures.CreateSqliteProvider(databasePath)
            : TenantScopeTestFixtures.CreateCoreProvider();
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(services);
        await TenantScopeTestFixtures.RegisterProductDefinitionAsync(services, TenantScopeTestFixtures.TenantB);

        var manager = services.GetRequiredService<IResourceManager>();
        var a1 = await TenantScopeTestFixtures.CreateProductAsync(services, "a1", "v1");
        a1 = await manager.UpdateAsync("a1", new UpdateResourceRequest { BaseVersion = a1.Version });
        await manager.UpdateAsync("a1", new UpdateResourceRequest { BaseVersion = a1.Version });
        await TenantScopeTestFixtures.CreateProductAsync(services, "a2", "other");
        await TenantScopeTestFixtures.CreateProductAsync(services, "a1", "tenant b", TenantScopeTestFixtures.TenantB);
        return services;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private sealed class RecordingReader(IResourceVersionReader inner) : IResourceVersionReader
    {
        public List<ResourceVersionReadRequest> Requests { get; } = [];

        public ValueTask<IEnumerable<Resource>> ReadVersionsAsync(
            ResourceVersionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.ReadVersionsAsync(request, cancellationToken);
        }
    }
}
