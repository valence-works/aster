using Aster.Core.Abstractions;
using Aster.Core.Definitions;
using Aster.Core.Exceptions;
using Aster.Core.Extensions;
using Aster.Core.Models.Definitions;
using Aster.Core.Models.Tenancy;
using Aster.Persistence.SqliteJson;
using Aster.Tests.SqliteJson;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Aster.Tests.Definitions;

/// <summary>
/// #92: <see cref="IAspectDefinitionStore"/> registers, versions and lists shared aspect definitions
/// identically in memory and in SQLite, per tenant, with facet metadata self-validation; SQLite startup
/// on an existing database adds the table without touching other data.
/// </summary>
public sealed class AspectDefinitionStoreTests : IDisposable
{
    private const string InMemory = "in-memory";
    private const string Sqlite = "sqlite";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"aster-{Guid.NewGuid():N}.db");

    public void Dispose() => SqliteJsonTestDatabase.DeleteFiles(databasePath);

    private ServiceProvider CreateProvider(string providerKind)
    {
        var services = new ServiceCollection().AddAsterCore();
        if (providerKind == Sqlite)
            services.AddAsterSqliteJson(options => options.ConnectionString = $"Data Source={databasePath}");

        return services.BuildServiceProvider();
    }

    private static AspectDefinition Seo(params FacetDefinition[] facets) => new()
    {
        AspectDefinitionId = "Seo",
        Id = Guid.NewGuid().ToString(),
        Version = 42,
        FacetDefinitions = facets.Length > 0 ? facets : [Facet("Slug", "string")],
    };

    private static FacetDefinition Facet(string id, string type, FacetMetadata? metadata = null) => new()
    {
        FacetDefinitionId = id,
        Id = Guid.NewGuid().ToString(),
        Version = 1,
        Type = type,
        Metadata = metadata,
    };

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Register_AppendsVersionsAndIgnoresTheSuppliedVersion(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();

        var first = await store.RegisterAsync(Seo());
        var second = await store.RegisterAsync(Seo(Facet("Slug", "string"), Facet("CanonicalUrl", "string")));

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal(second.Id, (await store.GetLatestAsync("Seo"))!.Id);
        Assert.Equal(first.Id, (await store.GetVersionAsync("Seo", 1))!.Id);
        Assert.Equal(new[] { "Slug", "CanonicalUrl" }, (await store.GetVersionAsync("Seo", 2))!.FacetDefinitions.Select(static f => f.FacetDefinitionId));
        Assert.Equal(new[] { 1, 2 }, (await store.ListVersionsAsync("Seo")).Select(static aspect => aspect.Version));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Register_MutatingTheCallersFacetListAfterwards_DoesNotChangeTheStoredVersion(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        var facets = new List<FacetDefinition> { Facet("Slug", "string"), Facet("Title", "string") };

        await store.RegisterAsync(Seo() with { FacetDefinitions = facets });
        facets.Clear();

        var stored = await store.GetVersionAsync("Seo", 1);
        Assert.Equal(new[] { "Slug", "Title" }, stored!.FacetDefinitions.Select(static facet => facet.FacetDefinitionId));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task List_ReturnsTheLatestVersionOfEachAspectInIdOrder(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        await store.RegisterAsync(Seo());
        await store.RegisterAsync(Seo() with { AspectDefinitionId = "Owner" });
        await store.RegisterAsync(Seo());
        await store.RegisterAsync(Seo() with { AspectDefinitionId = "Audit", RequiresName = true });

        var latest = await store.ListAsync();

        Assert.Equal(new[] { ("Audit", 1), ("Owner", 1), ("Seo", 2) }, latest.Select(static aspect => (aspect.AspectDefinitionId, aspect.Version)));
        Assert.True(latest[0].RequiresName);
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task MissingAspect_ReturnsNullOrEmpty(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        await store.RegisterAsync(Seo());

        Assert.Null(await store.GetLatestAsync("Nope"));
        Assert.Null(await store.GetVersionAsync("Seo", 2));
        Assert.Empty(await store.ListVersionsAsync("Nope"));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Tenants_AreIsolated(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();
        var tenantA = TenantScope.FromTenantId("tenant-a");
        var tenantB = TenantScope.FromTenantId("tenant-b");

        await store.RegisterAsync(Seo(), tenantA);
        await store.RegisterAsync(Seo(), tenantA);
        var inB = await store.RegisterAsync(Seo(), tenantB);

        Assert.Equal(1, inB.Version);
        Assert.Equal(2, (await store.GetLatestAsync("Seo", tenantA))!.Version);
        Assert.Null(await store.GetLatestAsync("Seo"));
        Assert.Empty(await store.ListAsync());
        Assert.Single(await store.ListAsync(tenantB));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Register_InvalidMetadata_IsRejectedWithStableCodes(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();

        var exception = await Assert.ThrowsAsync<FacetMetadataValidationException>(async () =>
            await store.RegisterAsync(Seo(
                Facet("Slug", "strnig"),
                Facet("Title", "string", new FacetMetadata { MinLength = 10, MaxLength = 5 }))));

        Assert.Equal(
            new[]
            {
                ("Seo", "Slug", FacetMetadataDiagnosticCodes.TypeUnknown),
                ("Seo", "Title", FacetMetadataDiagnosticCodes.RangeInvalid),
            },
            exception.Diagnostics.Select(static d => (d.AspectKey, d.FacetDefinitionId, d.Code)));
        Assert.Null(await store.GetLatestAsync("Seo"));
    }

    [Theory]
    [InlineData(InMemory)]
    [InlineData(Sqlite)]
    public async Task Register_BlankId_Throws(string providerKind)
    {
        await using var provider = CreateProvider(providerKind);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();

        await Assert.ThrowsAsync<ArgumentException>(async () => await store.RegisterAsync(Seo() with { AspectDefinitionId = " " }));
    }

    [Fact]
    public async Task Sqlite_PersistsAcrossProviderInstances()
    {
        await using (var first = CreateProvider(Sqlite))
            await first.GetRequiredService<IAspectDefinitionStore>().RegisterAsync(Seo());

        await using var second = CreateProvider(Sqlite);
        var store = second.GetRequiredService<IAspectDefinitionStore>();

        Assert.Equal(1, (await store.GetLatestAsync("Seo"))!.Version);
        Assert.Equal(2, (await store.RegisterAsync(Seo())).Version);
    }

    [Fact]
    public async Task Sqlite_StartupOnADatabaseWithoutTheTable_AddsItWithoutTouchingOtherData()
    {
        await using (var first = CreateProvider(Sqlite))
        {
            await first.GetRequiredService<IResourceDefinitionStore>().RegisterDefinitionAsync(
                new ResourceDefinitionBuilder().WithDefinitionId("Article").WithAspect("Seo").WithFacet("Slug", "string").Build());
        }

        // Simulate a database created before #92.
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using var drop = connection.CreateCommand();
            drop.CommandText = "DROP TABLE aspect_definitions;";
            await drop.ExecuteNonQueryAsync();
        }

        await using var second = CreateProvider(Sqlite);

        Assert.Equal(1, (await second.GetRequiredService<IResourceDefinitionStore>().GetDefinitionAsync("Article"))!.Version);
        Assert.Equal(1, (await second.GetRequiredService<IAspectDefinitionStore>().RegisterAsync(Seo())).Version);
    }

    [Fact]
    public async Task Sqlite_ConcurrentRegistrations_GetDistinctConsecutiveVersions()
    {
        await using var provider = CreateProvider(Sqlite);
        var store = provider.GetRequiredService<IAspectDefinitionStore>();

        var registered = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.RegisterAsync(Seo()).AsTask()));

        Assert.Equal(Enumerable.Range(1, 8), registered.Select(static aspect => aspect.Version).Order());
    }
}
